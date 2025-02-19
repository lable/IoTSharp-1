﻿using DotNetCore.CAP;
using EasyCaching.Core;
using IoTSharp.Data;
using IoTSharp.Extensions;
using IoTSharp.FlowRuleEngine;
using IoTSharp.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.AspNetCoreEx;
using MQTTnet.Server;
using MQTTnet.Server.Status;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IoTSharp.Handlers
{
    public class MQTTServerHandler
    {
        readonly ILogger<MQTTServerHandler> _logger;
        private readonly IServiceScopeFactory _scopeFactor;
        private readonly IEasyCachingProviderFactory _factory;
        readonly IMqttServerEx _serverEx;
        private readonly ICapPublisher _queue;
        private readonly FlowRuleProcessor _flowRuleProcessor;
        private readonly IEasyCachingProvider _caching;
        readonly MqttClientSetting _mcsetting;
        private readonly AppSettings _settings;

        public MQTTServerHandler(ILogger<MQTTServerHandler> logger, IServiceScopeFactory scopeFactor, IMqttServerEx serverEx
           , IOptions<AppSettings> options, ICapPublisher queue, IEasyCachingProviderFactory factory, FlowRuleProcessor flowRuleProcessor
            )
        {
            _mcsetting = options.Value.MqttClient;
            _settings = options.Value;
            _logger = logger;
            _scopeFactor = scopeFactor;
            _factory = factory;
            _serverEx = serverEx;
            _queue = queue;
            _flowRuleProcessor = flowRuleProcessor;
            _caching = factory.GetCachingProvider("iotsharp");
        }

        static long clients = 0;
        internal void Server_ClientConnected(object sender, MqttServerClientConnectedEventArgs e)
        {
            _logger.LogInformation($"Client [{e.ClientId}] connected");
            clients++;
            Task.Run(() => _serverEx.PublishAsync("$SYS/broker/clients/total", clients.ToString()));
        }
        static DateTime uptime = DateTime.MinValue;
        internal void Server_Started(object sender, EventArgs e)
        {
            _logger.LogInformation($"MqttServer is  started");
            uptime = DateTime.Now;
        }

        internal void Server_Stopped(object sender, EventArgs e)
        {
            _logger.LogInformation($"Server is stopped");
        }
        Dictionary<string, int> lstTopics = new Dictionary<string, int>();
        long received = 0;
        internal async void Server_ApplicationMessageReceived(object sender, MqttApplicationMessageReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.ClientId))
            {
                _logger.LogInformation($"ClientId为空,无法进一步获取设备信息 Topic=[{e.ApplicationMessage.Topic }]");
            }
            else
            {
                try
                {
                    _logger.LogInformation($"Server received {e.ClientId}'s message: Topic=[{e.ApplicationMessage.Topic }],Retain=[{e.ApplicationMessage.Retain}],QualityOfServiceLevel=[{e.ApplicationMessage.QualityOfServiceLevel}]");
                    if (!lstTopics.ContainsKey(e.ApplicationMessage.Topic))
                    {
                        lstTopics.Add(e.ApplicationMessage.Topic, 1);
                        await _serverEx.PublishAsync("$SYS/broker/subscriptions/count", lstTopics.Count.ToString());
                    }
                    else
                    {
                        lstTopics[e.ApplicationMessage.Topic]++;
                    }
                    if (e.ApplicationMessage.Payload != null)
                    {
                        received += e.ApplicationMessage.Payload.Length;
                    }
                    string topic = e.ApplicationMessage.Topic;
                    var tpary = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    var _dev = await FoundDevice(e.ClientId);
                    if (tpary.Length >= 3 && tpary[0] == "devices" && _dev != null)
                    {
                        Device device = JudgeOrCreateNewDevice(tpary, _dev);
                        if (device != null)
                        {
                            Dictionary<string, object> keyValues = new Dictionary<string, object>();
                            if (tpary.Length >= 4)
                            {
                                string keyname = tpary.Length >= 5 ? tpary[4] : tpary[3];
                                if (tpary[3].ToLower() == "xml")
                                {
                                    try
                                    {
                                        var xml = new System.Xml.XmlDocument();
                                        xml.LoadXml(e.ApplicationMessage.ConvertPayloadToString());
                                        keyValues.Add(keyname, xml);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, $"xml data error {topic},{ex.Message}");
                                    }
                                }
                                else if (tpary[3].ToLower() == "binary")
                                {
                                    keyValues.Add(keyname, e.ApplicationMessage.Payload);
                                }
                            }
                            else
                            {
                                try
                                {
                                    keyValues = e.ApplicationMessage.ConvertPayloadToDictionary();
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, $"ConvertPayloadToDictionary   Error {topic},{ex.Message}");
                                }
                            }
                            if (tpary[2] == "telemetry")
                            {
                                _queue.PublishTelemetryData(new RawMsg() { DeviceId = device.Id, MsgBody = keyValues, DataSide = DataSide.ClientSide, DataCatalog = DataCatalog.TelemetryData });
                            }
                            else if (tpary[2] == "attributes")
                            {
                                if (tpary.Length > 3 && tpary[3] == "request")
                                {
                                    await RequestAttributes(tpary, e.ApplicationMessage.ConvertPayloadToDictionary(), device);
                                }
                                else
                                {
                                    _queue.PublishAttributeData(new RawMsg() { DeviceId = device.Id, MsgBody = keyValues, DataSide = DataSide.ClientSide, DataCatalog = DataCatalog.AttributeData });
                                }

                            }
                            else if (tpary[2] == "rpc")
                            {

                            }
                            else
                            {
                                var rules = await _caching.GetAsync($"ruleid_{_dev.Id}_raw", async () =>
                                {
                                    using (var scope = _scopeFactor.CreateScope())
                                    using (var _dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>())
                                    {
                                        var guids = await _dbContext.GerDeviceRulesIdList(_dev.Id, MountType.RAW);
                                        return guids;
                                    }
                                }
                                , TimeSpan.FromSeconds(_settings.RuleCachingExpiration));
                                if (rules.HasValue)
                                {
                                    var obj = new { e.ApplicationMessage.Topic, Payload = Convert.ToBase64String(e.ApplicationMessage.Payload), e.ClientId };
                                    rules.Value.ToList().ForEach(async g =>
                                    {
                                        _logger.LogInformation($"{e.ClientId}的数据{e.ApplicationMessage.Topic}通过规则链{g}进行处理。");
                                        await _flowRuleProcessor.RunFlowRules(g, obj, _dev.Id, EventType.Normal, null);
                                    });
                                }
                                else
                                {
                                    _logger.LogInformation($"{e.ClientId}的数据{e.ApplicationMessage.Topic}不符合规范， 也无相关规则链处理。");
                                }
                                
                            }

                        }
                        else
                        {
                            _logger.LogInformation($"{e.ClientId}的数据{e.ApplicationMessage.Topic}未能匹配到设备");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"{e.ClientId}的数据{e.ApplicationMessage.Topic}未能识别,分段:{tpary.Length} 前缀?{tpary[0]}  设备:{_dev?.Id} ,终端状态未找到。");
                        var ss = await _serverEx.GetClientStatusAsync();
                        var status=  ss.FirstOrDefault(s => s.ClientId == e.ClientId);
                        if (status != null)
                        {
                            _logger.LogInformation($"{e.ClientId}的数据{e.ApplicationMessage.Topic}未能识别,分段:{tpary.Length} 前缀?{tpary[0]}  设备:{_dev?.Id} {status.ConnectedTimestamp} {status.Endpoint}   ");
                            if (!status.Session.Items.ContainsKey("iotsharp_count"))
                            {
                                status.Session.Items.Add("iotsharp_count", 1);
                            }
                            else
                            {
                                status.Session.Items["iotsharp_count"] = 1 + (int)status.Session.Items["iotsharp_count"];
                            }
                            if (status.Session.Items.TryGetValue("iotsharp_count", out object count))
                            {
                                int _count = (int)count;
                                if (_count > 10)
                                {
                                    await status.DisconnectAsync();
                                    _logger.LogInformation($"未识别次数太多{_count}");
                                }
                            }
                            else
                            {
                                _logger.LogInformation("识别次数获取错误");
                            }
                        }
                        else
                        {
                            _logger.LogInformation("设备状态未能获取");
                        }
                    }

                }
                catch (Exception ex)
                {
                    e.ProcessingFailed = true;
                    _logger.LogWarning(ex, $"ApplicationMessageReceived {ex.Message} {ex.InnerException?.Message}");
                }

            }
        }

        private async Task<Device> FoundDevice(string clientid)
        {
            var ss = await _serverEx.GetSessionStatusAsync();
            var _device = ss.FirstOrDefault(s => s.ClientId == clientid)?.Items?.FirstOrDefault(k => (string)k.Key == nameof(Device)).Value as Device;
            return _device;
        }

        private async Task RequestAttributes(string[] tpary, Dictionary<string, object> keyValues, Device device)
        {
            using (var scope = _scopeFactor.CreateScope())
            using (var _dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>())
            {
                if (tpary.Length > 5 && tpary[4] == "xml")
                {
                    var qf = from at in _dbContext.AttributeLatest where at.Type == DataType.XML && at.KeyName == tpary[5] select at;
                    await qf.LoadAsync();
                    await _serverEx.PublishAsync($"/devices/me/attributes/response/{tpary[5]}", qf.FirstOrDefault()?.Value_XML);
                }
                else if (tpary.Length > 5 && tpary[4] == "binary")
                {
                    var qf = from at in _dbContext.AttributeLatest where at.Type == DataType.Binary && at.KeyName == tpary[5] select at;
                    await qf.LoadAsync();
                    await _serverEx.PublishAsync(new MqttApplicationMessage() { Topic = $"/devices/me/attributes/response/{tpary[5]}", Payload = qf.FirstOrDefault()?.Value_Binary });
                }
                else
                {
                    Dictionary<string, object> reps = new Dictionary<string, object>();
                    var reqid = tpary.Length > 4 ? tpary[4] : Guid.NewGuid().ToString();
                    List<AttributeLatest> datas = new List<AttributeLatest>();
                    foreach (var kx in keyValues)
                    {
                        var keys = kx.Value?.ToString().Split(',');
                        if (keys != null && keys.Length > 0)
                        {
                            if (Enum.TryParse(kx.Key, true, out DataSide ds))
                            {
                                var qf = from at in _dbContext.AttributeLatest where at.DeviceId == device.Id && keys.Contains(at.KeyName) select at;
                                await qf.LoadAsync();
                                if (ds == DataSide.AnySide)
                                {
                                    datas.AddRange(await qf.ToArrayAsync());
                                }
                                else
                                {
                                    var qx = from at in qf where at.DataSide == ds select at;
                                    datas.AddRange(await qx.ToArrayAsync());
                                }
                            }
                        }
                    }


                    foreach (var item in datas)
                    {
                        switch (item.Type)
                        {
                            case DataType.Boolean:
                                reps.Add(item.KeyName, item.Value_Boolean);
                                break;
                            case DataType.String:
                                reps.Add(item.KeyName, item.Value_String);
                                break;
                            case DataType.Long:
                                reps.Add(item.KeyName, item.Value_Long);
                                break;
                            case DataType.Double:
                                reps.Add(item.KeyName, item.Value_Double);
                                break;
                            case DataType.Json:
                                reps.Add(item.KeyName, Newtonsoft.Json.Linq.JToken.Parse(item.Value_Json));
                                break;
                            case DataType.XML:
                                reps.Add(item.KeyName, item.Value_XML);
                                break;
                            case DataType.Binary:
                                reps.Add(item.KeyName, item.Value_Binary);
                                break;
                            case DataType.DateTime:
                                reps.Add(item.KeyName, item.Value_DateTime);
                                break;
                            default:
                                reps.Add(item.KeyName, item.Value_Json);
                                break;
                        }
                    }
                    await _serverEx.PublishAsync($"/devices/me/attributes/response/{reqid}", Newtonsoft.Json.JsonConvert.SerializeObject(reps));
                }
            }

        }

        internal async void Server_ClientDisconnected(IMqttServerEx server, MqttServerClientDisconnectedEventArgs args)
        {
         
                try
                {
                    var dev = await FoundDevice(args.ClientId);
                    if (dev != null)
                    {
                        using (var scope = _scopeFactor.CreateScope())
                        using (var _dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>())
                        {
                            var devtmp = _dbContext.Device.FirstOrDefault(d => d.Id == dev.Id);
                            devtmp.LastActive = DateTime.Now;
                            devtmp.Online = false;
                            _dbContext.SaveChanges();
                            _logger.LogInformation($"Server_ClientDisconnected   ClientId:{args.ClientId} DisconnectType:{args.DisconnectType}  Device is {devtmp.Name }({devtmp.Id}) ");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Server_ClientDisconnected ClientId:{args.ClientId} DisconnectType:{args.DisconnectType}, 未能在缓存中找到");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Server_ClientDisconnected ClientId:{args.ClientId} DisconnectType:{args.DisconnectType},{ex.Message}");

                }
        }

        private Device JudgeOrCreateNewDevice(string[] tpary, Device device)
        {
            Device devicedatato = null;
            using (var scope = _scopeFactor.CreateScope())
            using (var _dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>())
            {

                if (tpary[1] != "me" && device.DeviceType == DeviceType.Gateway)
                {
                    var ch = from g in _dbContext.Gateway.Include(g => g.Tenant).Include(g => g.Customer).Include(c => c.Children) where g.Id == device.Id select g;
                    var gw = ch.FirstOrDefault();
                    var subdev = from cd in gw.Children where cd.Name == tpary[1] select cd;
                    if (!subdev.Any())
                    {
                        devicedatato = new Device() { Id = Guid.NewGuid(), Name = tpary[1], DeviceType = DeviceType.Device, Tenant = gw.Tenant, Customer = gw.Customer, Owner = gw,  LastActive = DateTime.Now, Timeout = 300 };
                        gw.Children.Add(devicedatato);
                        _dbContext.AfterCreateDevice(devicedatato);
                        gw.LastActive = DateTime.Now;
                        gw.Online = true;
                    }
                    else
                    {
                        devicedatato = subdev.FirstOrDefault();
                        devicedatato.LastActive = DateTime.Now;
                        devicedatato.Online = true;
                    }
                }
                else
                {
                    devicedatato = _dbContext.Device.Find(device.Id);
                    devicedatato.LastActive = DateTime.Now;
                    devicedatato.Online = true;
                }
                _dbContext.SaveChanges();
            }
            return devicedatato;
        }

        long Subscribed;
        internal  async  Task Server_ClientSubscribedTopic(object sender, MqttServerClientSubscribedTopicEventArgs e)
        {
            _logger.LogInformation($"Client [{e.ClientId}] subscribed [{e.TopicFilter}]");

            if (e.TopicFilter.Topic.StartsWith("$SYS/"))
            {
                if (e.TopicFilter.Topic.StartsWith("$SYS/broker/version"))
                {
                    var mename = typeof(MQTTServerHandler).Assembly.GetName();
                    var mqttnet = typeof(MqttServerClientSubscribedTopicEventArgs).Assembly.GetName();
                    await _serverEx.PublishAsync("$SYS/broker/version", $"{mename.Name}V{mename.Version.ToString()},{mqttnet.Name}.{mqttnet.Version.ToString()}");
                }
                else if (e.TopicFilter.Topic.StartsWith("$SYS/broker/uptime"))
                {
                    await _serverEx.PublishAsync("$SYS/broker/uptime", uptime.ToString());
                }
            }
            if (e.TopicFilter.Topic.ToLower().StartsWith("/devices/telemetry"))
            {


            }
            else
            {
                Subscribed++;
               await  _serverEx.PublishAsync("$SYS/broker/subscriptions/count", Subscribed.ToString());
            }
        }

        internal void Server_ClientUnsubscribedTopic(object sender, MqttServerClientUnsubscribedTopicEventArgs e)
        {
            _logger.LogInformation($"Client [{e.ClientId}] unsubscribed[{e.TopicFilter}]");
            if (!e.TopicFilter.StartsWith("$SYS/"))
            {
                Subscribed--;
                Task.Run(() => _serverEx.PublishAsync("$SYS/broker/subscriptions/count", Subscribed.ToString()));
            }
        }
         
         
       
        public static string MD5Sum(string text) => BitConverter.ToString(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        internal void Server_ClientConnectionValidator(object sender, MqttServerClientConnectionValidatorEventArgs e)
        {
            try
            {
                using (var scope = _scopeFactor.CreateScope())
                using (var _dbContextcv = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>())
                {
                    MqttConnectionValidatorContext obj = e.Context;

                    // jy 特殊处理 ::1
                    var isLoopback = false;
                    if (obj.Endpoint?.StartsWith("::1") == true)
                    {
                        isLoopback = true;
                    }
                    else
                    {
                        Uri uri = new Uri("mqtt://" + obj.Endpoint);
                        isLoopback = uri.IsLoopback;
                    }
                    if (isLoopback && !string.IsNullOrEmpty(e.Context.ClientId) && e.Context.ClientId == _mcsetting.MqttBroker && !string.IsNullOrEmpty(e.Context.Username) && e.Context.Username == _mcsetting.UserName && e.Context.Password == _mcsetting.Password)
                    {
                        e.Context.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.Success;
                    }
                    else
                    {
                        _logger.LogInformation($"ClientId={obj.ClientId},Endpoint={obj.Endpoint},Username={obj.Username}，Password={obj.Password},WillMessage={obj.WillMessage?.ConvertPayloadToString()}");
                        var mcr = _dbContextcv.DeviceIdentities.Include(d => d.Device).FirstOrDefault(mc =>
                                              (mc.IdentityType == IdentityType.AccessToken && mc.IdentityId == obj.Username) ||
                                              (mc.IdentityType == IdentityType.DevicePassword && mc.IdentityId == obj.Username && mc.IdentityValue == obj.Password));
                        if (mcr != null)
                        {
                            try
                            {
                                var device = mcr.Device;
                                e.Context.SessionItems.TryAdd(nameof(Device), device);
                                e.Context.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.Success;
                                _logger.LogInformation($"Device {device.Name}({device.Id}) is online !username is {obj.Username} and  is endpoint{obj.Endpoint}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "ConnectionRefusedServerUnavailable {0}", ex.Message);
                                e.Context.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.ServerUnavailable;
                            }
                        }
                        else if (_dbContextcv.AuthorizedKeys.Any(ak => ak.AuthToken == obj.Password))
                        {
                            var ak = _dbContextcv.AuthorizedKeys.Include(ak => ak.Customer).Include(ak => ak.Tenant).Include(ak => ak.Devices).FirstOrDefault(ak => ak.AuthToken == obj.Password);
                            if (ak != null && !ak.Devices.Any(dev => dev.Name == obj.Username))
                            {

                                var devvalue = new Device() { Name = obj.Username, DeviceType = DeviceType.Device, Timeout = 300, LastActive = DateTime.Now };
                                devvalue.Tenant = ak.Tenant;
                                devvalue.Customer = ak.Customer;
                                _dbContextcv.Device.Add(devvalue);
                                ak.Devices.Add(devvalue);
                                _dbContextcv.AfterCreateDevice(devvalue, obj.Username, obj.Password);
                                _dbContextcv.SaveChanges();
                            }
                            var mcp = _dbContextcv.DeviceIdentities.Include(d => d.Device).FirstOrDefault(mc => mc.IdentityType == IdentityType.DevicePassword && mc.IdentityId == obj.Username && mc.IdentityValue == obj.Password);
                            if (mcp != null)
                            {
                                e.Context.SessionItems.TryAdd(nameof(Device), mcp.Device);
                                e.Context.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.Success;
                                _logger.LogInformation($"Device {mcp.Device.Name}({mcp.Device.Id}) is online !username is {obj.Username} and  is endpoint{obj.Endpoint}");
                            }
                            else
                            {
                                e.Context.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.BadUserNameOrPassword;
                                _logger.LogInformation($"Bad username or  password/AuthToken {obj.Username},connection {obj.Endpoint} refused");
                            }
                        }
                        else
                        {

                            e.Context.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.BadUserNameOrPassword;
                            _logger.LogInformation($"Bad username or password {obj.Username},connection {obj.Endpoint} refused");
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                e.Context.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.ImplementationSpecificError;
                e.Context.ReasonString = ex.Message;
                _logger.LogError(ex, "ConnectionRefusedServerUnavailable {0}", ex.Message);
            }


        }


        public Task<IList<MqttApplicationMessage>> GetRetainedMessagesAsync()
        {
            return _serverEx.GetRetainedApplicationMessagesAsync();
        }

        public Task DeleteRetainedMessagesAsync()
        {
            return _serverEx.ClearRetainedApplicationMessagesAsync();
        }

        private async Task Publish(MqttApplicationMessage message)
        {
            await _serverEx.PublishAsync(message);
            _logger.Log(LogLevel.Trace, $"Published MQTT topic '{message.Topic}.");
        }




        public Task<IList<IMqttClientStatus>> GetClientsAsync()
        {
            return _serverEx.GetClientStatusAsync();
        }

        public Task<IList<IMqttSessionStatus>> GetSessionsAsync()
        {
            return _serverEx.GetSessionStatusAsync();
        }
    }
}
