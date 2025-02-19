﻿using IoTSharp.Controllers.Models;
using IoTSharp.Data;
using IoTSharp.Dtos;
using IoTSharp.Extensions;
using IoTSharp.FlowRuleEngine;
using IoTSharp.Models;
using IoTSharp.Models.Rule;
using LinqKit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Castle.Components.DictionaryAdapter;
using Dynamitey.DynamicObjects;
using Namotion.Reflection;
using IoTSharp.TaskAction;

namespace IoTSharp.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class RulesController : ControllerBase
    {
        private ApplicationDbContext _context;
        private readonly FlowRuleProcessor _flowRuleProcessor;
        private readonly TaskExecutorHelper _helper;
        private UserManager<IdentityUser> _userManager;

        public RulesController(ApplicationDbContext context, UserManager<IdentityUser> userManager, FlowRuleProcessor flowRuleProcessor, TaskExecutorHelper helper)
        {
            this._userManager = userManager;
            this._context = context;
            _flowRuleProcessor = flowRuleProcessor;
            _helper = helper;
        }


        /// <summary>
        /// 更新节点的条件表达式
        /// </summary>
        /// <returns> </returns>
        /// 

        [HttpPost("[action]")]
        public async Task<ApiResult<bool>> UpdateFlowExpression( UpdateFlowExpression m)
        {
            var flow = await _context.Flows.SingleOrDefaultAsync(c => c.FlowId == m.FlowId);
            if (flow != null)
            {
                flow.Conditionexpression = m.Expression;
                _context.Flows.Update(flow);
                await _context.SaveChangesAsync();

                return new ApiResult<bool>(ApiCode.Success, "Ok", true);
            }
            return new ApiResult<bool>(ApiCode.InValidData, "can't find this object", false);
        }

        [HttpPost("[action]")]
        public async Task<ApiResult<PagedData<FlowRule>>> Index([FromBody] RulePageParam m)
        {
            var profile = await this.GetUserProfile();

            Expression<Func<FlowRule, bool>> condition = x => x.RuleStatus > -1;
            if (!string.IsNullOrEmpty(m.Name))
            {
                condition = condition.And(x => x.Name.Contains(m.Name));
            }

            if (m.CreatTime != null && m.CreatTime.Length == 2)
            {
                condition = condition.And(x => x.CreatTime > m.CreatTime[0] && x.CreatTime < m.CreatTime[1]);
            }

            if (!string.IsNullOrEmpty(m.Creator))
            {
                condition = condition.And(x => x.Creator == m.Creator);
            }

            return new ApiResult<PagedData<FlowRule>>(ApiCode.Success, "OK", new PagedData<FlowRule>
            {
                total = _context.FlowRules.Count(condition),
                rows = _context.FlowRules.OrderByDescending(c => c.CreatTime).Where(condition).Skip((m.offset) * m.limit).Take(m.limit).ToList()
            });
        }

        [HttpPost("[action]")]
        public ApiResult<bool> Save(FlowRule m)
        {
            try
            {

                m.MountType = m.MountType;
                m.RuleStatus = 1;
                m.CreatTime = DateTime.Now;
                _context.FlowRules.Add(m);
                _context.SaveChanges();

                return new ApiResult<bool>(ApiCode.Success, "OK", true);

            }
            catch (Exception ex)
            {

                return new ApiResult<bool>(ApiCode.Exception, ex.Message, false);
            }



        }

        [HttpPost("[action]")]
        public ApiResult<bool> Update(FlowRule m)
        {

            var flowrule = _context.FlowRules.SingleOrDefault(c => c.RuleId == m.RuleId);
            if (flowrule != null)
            {
                try
                {
                    flowrule.MountType = m.MountType;
                    flowrule.Name = m.Name;
                    flowrule.RuleDesc = m.RuleDesc;
                    _context.FlowRules.Update(flowrule);
                    _context.SaveChanges();
                    return new ApiResult<bool>(ApiCode.Success, "OK", true);
                }
                catch (Exception ex)
                {
                    return new ApiResult<bool>(ApiCode.Exception, ex.Message, false);
                }
            }
            return new ApiResult<bool>(ApiCode.Success, "can't find this object", false);
        }

        [HttpGet("[action]")]
        public ApiResult<bool> Delete(Guid id)
        {
            var rule = _context.FlowRules.SingleOrDefault(c => c.RuleId == id);
            if (rule != null)
            {
                try
                {
                    rule.RuleStatus = -1;
                    _context.FlowRules.Update(rule);
                    _context.SaveChanges();
                    return new ApiResult<bool>(ApiCode.Success, "OK", true);
                }
                catch (Exception ex)
                {
                    return new ApiResult<bool>(ApiCode.Exception, ex.Message, false);
                }
            }

            return new ApiResult<bool>(ApiCode.Success, "can't find this object", false);
        }

        [HttpGet("[action]")]
        public ApiResult<FlowRule> Get(Guid id)
        {
            var rule = _context.FlowRules.SingleOrDefault(c => c.RuleId == id);
            if (rule != null)
            {
                return new ApiResult<FlowRule>(ApiCode.Success, "OK", rule);
            }

            return new ApiResult<FlowRule>(ApiCode.CantFindObject, "can't find this object", null);
        }
        /// <summary>
        /// 复制一个规则副本
        /// </summary>
        /// <param name="flowRule"></param>
        /// <returns></returns>


        [HttpPost("[action]")]
        public async Task<ApiResult<bool>> Fork(FlowRule flowRule)
        {
            var profile = await this.GetUserProfile();
            var rule = await _context.FlowRules.SingleOrDefaultAsync(c => c.RuleId == flowRule.RuleId);
            if (rule != null)
            {
                var newrule = new FlowRule();
                newrule.DefinitionsXml = rule.DefinitionsXml;
                newrule.Describes = flowRule.Describes;
                //     newrule.Creator = profile.Id.ToString();
                newrule.Name = flowRule.Name;
                newrule.CreatTime = DateTime.Now;
                newrule.ExecutableCode = rule.ExecutableCode;
                newrule.RuleDesc = flowRule.RuleDesc;
                newrule.RuleStatus = 1;
                newrule.MountType = flowRule.MountType;
                newrule.ParentRuleId = rule.RuleId;
                newrule.CreateId = new Guid();
                newrule.SubVersion = rule.SubVersion + 0.01;
                newrule.Runner = rule.Runner;
                _context.FlowRules.Add(newrule);
                await _context.SaveChangesAsync();
          
                var flows = _context.Flows.Where(c => c.FlowRule.RuleId == rule.RuleId&& c.CreateId== rule.CreateId).ToList();
                var newflows = flows.Select(c => new Flow()
                {
                    FlowRule = newrule,
                    Conditionexpression = c.Conditionexpression,
                    CreateDate = DateTime.Now,
                    FlowStatus = 1,
                    FlowType = c.FlowType,
                    Flowdesc = c.Flowdesc,
                    Incoming = c.Incoming,
                    Flowname = c.Flowname,
                    NodeProcessClass = c.NodeProcessClass,
                    NodeProcessMethod = c.NodeProcessMethod,
                    NodeProcessParams = c.NodeProcessParams,
                    NodeProcessScript = c.NodeProcessScript,
                    NodeProcessScriptType = c.NodeProcessScriptType,
                    NodeProcessType = c.NodeProcessType,
                    ObjectId = c.ObjectId,
                    Outgoing = c.Outgoing,
                    SourceId = c.SourceId,
                    TargetId = c.TargetId,
                     
                    bpmnid = c.bpmnid,
                    CreateId = newrule.CreateId

                }).ToList();
                if (newflows.Count > 0)
                {
                    _context.Flows.AddRange(newflows);
                    await _context.SaveChangesAsync();
                }

                return new ApiResult<bool>(ApiCode.Success, "Ok", true);

            }
            else
            {

            }


            return new ApiResult<bool>(ApiCode.CantFindObject, "can't find this object", false);
        }



        [HttpPost("[action]")]
        public async Task<ApiResult<bool>> BindDevice(ModelRuleBind m)
        {
            var profile = await this.GetUserProfile();
            if (m.dev != null)

            {
                m.dev.ToList().ForEach(d =>
                {
                    if (!_context.DeviceRules.Any(c => c.FlowRule.RuleId == m.rule && c.Device.Id == d))
                    {
                        var dr = new DeviceRule();
                        dr.Device = _context.Device.SingleOrDefault(c => c.Id == d);
                        dr.FlowRule = _context.FlowRules.SingleOrDefault(c => c.RuleId == m.rule);
                        dr.ConfigDateTime = DateTime.Now;
                        dr.ConfigUser = profile.Id;
                        _context.DeviceRules.Add(dr);
                        _context.SaveChanges();
                    }
                });

                return new ApiResult<bool>(ApiCode.Success, "rule binding success", true);
            }

            return new ApiResult<bool>(ApiCode.CantFindObject, "No device found", false);
        }

        [HttpGet("[action]")]
        public async Task<ApiResult<bool>> DeleteDeviceRules(Guid deviceId, Guid ruleId)
        {
            var profile = await this.GetUserProfile();
            var map = _context.DeviceRules.FirstOrDefault(c => c.FlowRule.RuleId == ruleId && c.Device.Id == deviceId);
            if (map != null)
            {
                _context.DeviceRules.Remove(map);
                _context.SaveChanges();
                return new ApiResult<bool>(ApiCode.Success, "rule has been removed", true);
            }
            return new ApiResult<bool>(ApiCode.CantFindObject, "this mapping was not found", true);
        }

        [HttpGet("[action]")]
        public ApiResult<List<FlowRule>> GetDeviceRules(Guid deviceId)
        {
            return new ApiResult<List<FlowRule>>(ApiCode.Success, "Ok", _context.DeviceRules.Where(c => c.Device.Id == deviceId).Select(c => c.FlowRule).Select(c=>new FlowRule(){ RuleId = c.RuleId, CreatTime = c.CreatTime, Name = c.Name, RuleDesc = c.RuleDesc}).ToList());
        }

        [HttpGet("[action]")]
        public ApiResult<List<Device>> GetRuleDevices(Guid ruleId)
        {
            return new ApiResult<List<Device>>(ApiCode.Success, "Ok", _context.DeviceRules.Where(c => c.FlowRule.RuleId == ruleId).Select(c => c.Device).ToList());
        }




        [HttpGet("[action]")]
        public ApiResult<List<Flow>> GetFlows(Guid ruleId)
        {
            return new ApiResult<List<Flow>>(ApiCode.Success, "Ok", _context.Flows.Include(c => c.FlowRule).Where(c => c.FlowRule.RuleId == ruleId && c.FlowStatus > 0).ToList());
        }


        [HttpPost("[action]")]
        public async Task<ApiResult<bool>> SaveDiagram(ModelWorkFlow m)
        {
            //    var user = _userManager.GetUserId(User);
            var profile = await this.GetUserProfile();
            var activity = JsonConvert.DeserializeObject<IoTSharp.Models.Rule.Activity>(m.Biz);
            var CreatorId = Guid.NewGuid();
            var CreateDate = DateTime.Now;
            var rule = _context.FlowRules.FirstOrDefault(c => c.RuleId == activity.RuleId);
         
            rule.DefinitionsXml = m.Xml;
            rule.Creator = profile.Id.ToString();

            rule.CreateId = CreatorId;


            //   _context.Flows.RemoveRange(_context.Flows.Where(c => c.FlowRule.RuleId == rule.RuleId).ToList());

            _context.Flows.Where(c => c.FlowRule.RuleId == rule.RuleId).ToList().ForEach(c =>
            {
                c.FlowStatus = -1;
            });

            _context.SaveChanges();

            _context.FlowRules.Update(rule);
            _context.SaveChanges();


            if (activity.BaseBpmnObjects != null)
            {
                _context.Flows.AddRange(activity.BaseBpmnObjects.Select(c => new Flow
                {
                    FlowRule = rule,
                    Flowname = c.BizObject.Flowname,
                    bpmnid = c.id,
                    FlowType = c.bpmntype,
                    NodeProcessScript = c.BizObject.flowscript,
                    NodeProcessScriptType = c.BizObject.flowscripttype,
                    FlowStatus = 1,
                    CreateId = CreatorId,
                    Createor = profile.Id,
                    CreateDate = CreateDate
                }).ToList());
                _context.SaveChanges();
            }

            if (activity.StartEvents != null)
            {
                _context.Flows.AddRange(activity.StartEvents.Select(c => new Flow
                {
                    FlowRule = rule,
                    Flowname = c.BizObject.Flowname,
                    bpmnid = c.id,
                    FlowType = c.bpmntype,
                    FlowStatus = 1,
                    CreateId = CreatorId,
                    Createor = profile.Id,
                    CreateDate = CreateDate
                }).ToList());
                _context.SaveChanges();
            }

            if (activity.EndEvents != null)
            {
                _context.Flows.AddRange(activity.EndEvents.Select(c => new Flow
                {
                    FlowRule = rule,
                    Flowname = c.BizObject.Flowname,
                    bpmnid = c.id,
                    FlowType = c.bpmntype,
                    FlowStatus = 1,
                    CreateId = CreatorId,
                    Createor = profile.Id,
                    CreateDate = CreateDate
                }).ToList());
                _context.SaveChanges();
            }

            if (activity.SequenceFlows != null)
            {
                _context.Flows.AddRange(activity.SequenceFlows.Select(c => new Flow
                {
                    FlowRule = rule,
                    Flowname = c.BizObject.Flowname,
                    bpmnid = c.id,
                    FlowType = c.bpmntype,
                    SourceId = c.sourceId,
                    TargetId = c.targetId,
                    FlowStatus = 1,
                    CreateId = CreatorId,
                    Conditionexpression = c.BizObject.conditionexpression,
                    NodeProcessParams = c.BizObject.NodeProcessParams,
                    Createor = profile.Id,
                    CreateDate = CreateDate
                }).ToList());
                _context.SaveChanges();
            }

            if (activity.Tasks != null)
            {
                _context.Flows.AddRange(activity.Tasks.Select(c => new Flow
                {
                    FlowRule = rule,
                    Flowname = c.BizObject.Flowname,
                    bpmnid = c.id,
                    FlowType = c.bpmntype,
                    NodeProcessParams = c.BizObject.NodeProcessParams,
                    NodeProcessClass = c.BizObject.NodeProcessClass,
                    NodeProcessScript = c.BizObject.flowscript,
                    NodeProcessScriptType = c.BizObject.flowscripttype,
                    FlowStatus = 1,
                    CreateId = CreatorId,
                    Createor = profile.Id,
                    CreateDate = CreateDate
                }).ToList());
                _context.SaveChanges();
            }

            if (activity.DataInputAssociations != null)
            {
                _context.Flows.AddRange(activity.DataInputAssociations.Select(c => new Flow
                {
                    FlowRule = rule,
                    Flowname = c.BizObject.Flowname,
                    bpmnid = c.id,
                    FlowType = c.bpmntype,
                    FlowStatus = 1,
                    CreateId = CreatorId,
                    Createor = profile.Id,
                    CreateDate = CreateDate
                }).ToList());
                _context.SaveChanges();
            }

            if (activity.DataOutputAssociations != null)
            {
                _context.Flows.AddRange(activity.DataOutputAssociations.Select(c => new Flow
                {
                    FlowRule = rule,
                    Flowname = c.BizObject.Flowname,
                    bpmnid = c.id,
                    FlowType = c.bpmntype,
                    FlowStatus = 1,
                    CreateId = CreatorId,
                    Createor = profile.Id,
                    CreateDate = CreateDate
                }).ToList());
                _context.SaveChanges();
            }

            if (activity.TextAnnotations != null)
            {
                _context.Flows.AddRange(activity.TextAnnotations.Select(c => new Flow
                {
                    FlowRule = rule,
                    Flowname = c.BizObject.Flowname,
                    bpmnid = c.id,
                    FlowType = c.bpmntype,
                    FlowStatus = 1,
                    CreateId = CreatorId,
                    Createor = profile.Id,
                    CreateDate = CreateDate

                }).ToList());
                _context.SaveChanges();
            }

            if (activity.Containers != null)
            {
                _context.Flows.AddRange(activity.Containers.Select(c => new Flow
                {
                    FlowRule = rule,
                    Flowname = c.BizObject.Flowname,
                    bpmnid = c.id,
                    FlowType = c.bpmntype,
                    FlowStatus = 1,
                    CreateId = CreatorId,
                    Createor = profile.Id,
                    CreateDate = CreateDate
                }).ToList());
                _context.SaveChanges();
            }

            if (activity.GateWays != null)
            {
                _context.Flows.AddRange(activity.GateWays.Select(c => new Flow
                {
                    FlowRule = rule,
                    Flowname = c.BizObject.Flowname,
                    bpmnid = c.id,
                    FlowType = c.bpmntype,
                    FlowStatus = 1,
                    CreateId = CreatorId,
                    Createor = profile.Id,
                    CreateDate = CreateDate
                }).ToList());
                _context.SaveChanges();
            }

            if (activity.DataStoreReferences != null)
            {
                _context.Flows.AddRange(activity.DataStoreReferences.Select(c => new Flow
                {
                    FlowRule = rule,
                    Flowname = c.BizObject.Flowname,
                    bpmnid = c.id,
                    FlowType = c.bpmntype,
                    FlowStatus = 1,
                    CreateId = CreatorId,
                    Createor = profile.Id,
                    CreateDate = CreateDate

                }).ToList());
                _context.SaveChanges();
            }

            if (activity.Lane != null)
            {
                _context.Flows.AddRange(activity.Lane.Select(c => new Flow
                {
                    FlowRule = rule,
                    Flowname = c.BizObject.Flowname,
                    bpmnid = c.id,
                    FlowType = c.bpmntype,
                    FlowStatus = 1,
                    CreateId = CreatorId,
                    Createor = profile.Id,
                    CreateDate = CreateDate
                }).ToList());
                _context.SaveChanges();
            }

            if (activity.LaneSet != null)
            {
                _context.Flows.AddRange(activity.LaneSet.Select(c => new Flow
                {
                    FlowRule = rule,
                    Flowname = c.BizObject.Flowname,
                    bpmnid = c.id,
                    FlowType = c.bpmntype,
                    FlowStatus = 1,
                    CreateId = CreatorId,
                    Createor = profile.Id,
                    CreateDate = CreateDate
                }).ToList());
                _context.SaveChanges();
            }
            return new ApiResult<bool>(ApiCode.Success, "Ok", true);
        }

        [HttpGet("[action]")]
        public ApiResult<Activity> GetDiagram(Guid id)
        {
            var ruleflow = _context.FlowRules.FirstOrDefault(c => c.RuleId == id);
            IoTSharp.Models.Rule.Activity activity = new IoTSharp.Models.Rule.Activity();

            activity.SequenceFlows ??= new List<SequenceFlow>();
            activity.GateWays ??= new List<GateWay>();
            activity.Tasks ??= new List<IoTSharp.Models.Rule.BaseTask>();
            activity.LaneSet ??= new List<BpmnBaseObject>();
            activity.EndEvents ??= new List<BpmnBaseObject>();
            activity.StartEvents ??= new List<BpmnBaseObject>();
            activity.Containers ??= new List<BpmnBaseObject>();
            activity.BaseBpmnObjects ??= new List<BpmnBaseObject>();
            activity.DataStoreReferences ??= new List<BpmnBaseObject>();
            activity.SubProcesses ??= new List<BpmnBaseObject>();
            activity.DataOutputAssociations ??= new List<BpmnBaseObject>();
            activity.DataInputAssociations ??= new List<BpmnBaseObject>();
            activity.Lane ??= new List<BpmnBaseObject>();
            activity.TextAnnotations ??= new List<BpmnBaseObject>();
            activity.RuleId = id;
            var flows = _context.Flows.Where(c => c.FlowRule.RuleId == id && c.FlowStatus > 0).ToList();
            activity.Xml = ruleflow.DefinitionsXml?.Trim('\r');
            foreach (var item in flows)
            {
                switch (item.FlowType)
                {
                    case "bpmn:SequenceFlow":

                        activity.SequenceFlows.Add(
                            new SequenceFlow()
                            {
                                sourceId = item.SourceId,
                                targetId = item.TargetId,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    conditionexpression = item.Conditionexpression
                                }
                            });
                        break;

                    case "bpmn:EndEvent":
                        activity.EndEvents.Add(

                            new GateWay
                            {
                                sourceId = item.SourceId,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    NodeProcessClass = item.NodeProcessClass,
                                }
                            });
                        break;

                    case "bpmn:StartEvent":
                        activity.StartEvents.Add(

                            new GateWay
                            {
                                sourceId = item.SourceId,

                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                }
                            });

                        break;

                    case "bpmn:ExclusiveGateway":
                        activity.GateWays.Add(

                            new GateWay
                            {
                                sourceId = item.SourceId,

                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    NodeProcessClass = item.NodeProcessClass
                                }
                            });
                        break;

                    case "bpmn:ParallelGateway":
                        activity.GateWays.Add(

                            new GateWay
                            {
                                sourceId = item.SourceId,

                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    NodeProcessClass = item.NodeProcessClass
                                }
                            });
                        break;

                    case "bpmn:InclusiveGateway":
                        activity.GateWays.Add(

                            new GateWay
                            {
                                sourceId = item.SourceId,

                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    NodeProcessClass = item.NodeProcessClass
                                }
                            });
                        break;

                    case "bpmn:EventBasedGateway":
                        activity.GateWays.Add(

                            new GateWay
                            {
                                sourceId = item.SourceId,

                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    NodeProcessClass = item.NodeProcessClass
                                }
                            });
                        break;

                    case "bpmn:ComplexGateway":
                        activity.GateWays.Add(

                            new GateWay
                            {
                                sourceId = item.SourceId,

                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    NodeProcessClass = item.NodeProcessClass
                                }
                            });
                        break;

                    case "bpmn:Task":
                        activity.Tasks.Add(

                            new IoTSharp.Models.Rule.BaseTask
                            {
                                id = item.bpmnid,
                                Flowname = item.Flowname,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    flowscript = item.NodeProcessScript,
                                    flowscripttype = item.NodeProcessScriptType,
                                    NodeProcessClass = item.NodeProcessClass,
                                    NodeProcessParams = item.NodeProcessParams

                                }
                            });
                        break;

                    case "bpmn:BusinessRuleTask":
                        activity.Tasks.Add(

                            new IoTSharp.Models.Rule.BaseTask
                            {
                                Flowname = item.Flowname,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    flowscript = item.NodeProcessScript,
                                    flowscripttype = item.NodeProcessScriptType,
                                    NodeProcessClass = item.NodeProcessClass
                                }
                            });
                        break;

                    case "bpmn:ReceiveTask":
                        activity.Tasks.Add(

                            new IoTSharp.Models.Rule.BaseTask
                            {
                                Flowname = item.Flowname,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    NodeProcessClass = item.NodeProcessClass
                                }
                            });
                        break;

                    case "bpmn:UserTask":
                        activity.Tasks.Add(

                            new IoTSharp.Models.Rule.BaseTask
                            {
                                Flowname = item.Flowname,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    flowscript = item.NodeProcessScript,
                                    flowscripttype = item.NodeProcessScriptType,
                                    NodeProcessClass = item.NodeProcessClass
                                }
                            });
                        break;

                    case "bpmn:ServiceTask":
                        activity.Tasks.Add(

                            new IoTSharp.Models.Rule.BaseTask
                            {
                                Flowname = item.Flowname,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    flowscript = item.NodeProcessScript,
                                    flowscripttype = item.NodeProcessScriptType,
                                    NodeProcessClass = item.NodeProcessClass
                                }
                            });
                        break;

                    case "bpmn:ManualTask":
                        activity.Tasks.Add(

                            new IoTSharp.Models.Rule.BaseTask
                            {
                                Flowname = item.Flowname,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    flowscript = item.NodeProcessScript,
                                    flowscripttype = item.NodeProcessScriptType,
                                    NodeProcessClass = item.NodeProcessClass
                                }
                            });
                        break;

                    case "bpmn:SendTask":
                        activity.Tasks.Add(

                            new IoTSharp.Models.Rule.BaseTask
                            {
                                Flowname = item.Flowname,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                    flowscript = item.NodeProcessScript,
                                    flowscripttype = item.NodeProcessScriptType,
                                    NodeProcessClass = item.NodeProcessClass
                                }
                            });
                        break;

                    case "bpmn:CallActivity":
                        activity.Tasks.Add(

                            new IoTSharp.Models.Rule.BaseTask
                            {
                                Flowname = item.Flowname,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                }
                            });
                        break;

                    case "bpmn:IntermediateCatchEvent":
                        activity.EndEvents.Add(

                            new BpmnBaseObject
                            {
                                Flowname = item.Flowname,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                }
                            });
                        break;

                    case "bpmn:IntermediateThrowEvent":
                        activity.EndEvents.Add(

                            new BpmnBaseObject()
                            {
                                Flowname = item.Flowname,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                }
                            });
                        break;

                    case "bpmn:Lane":
                        activity.Containers.Add(

                            new BpmnBaseObject
                            {
                                Flowname = item.Flowname,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                }
                            });
                        break;

                    case "bpmn:Participant":
                        activity.Containers.Add(

                            new BpmnBaseObject
                            {
                                Flowname = item.Flowname,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                }
                            });
                        break;

                    case "bpmn:DataStoreReference":
                        activity.DataStoreReferences.Add(

                            new BpmnBaseObject
                            {
                                Flowname = item.Flowname,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                }
                            });
                        break;

                    case "bpmn:SubProcess":
                        activity.SubProcesses.Add(

                            new BpmnBaseObject
                            {
                                Flowname = item.Flowname,
                                id = item.bpmnid,
                                bpmntype = item.FlowType,
                                BizObject = new FormBpmnObject
                                {
                                    Flowid = item.FlowId.ToString(),
                                    Flowdesc = item.Flowdesc,
                                    Flowtype = item.FlowType,
                                    Flowname = item.Flowname,
                                }
                            });
                        break;

                    default:
                        BpmnBaseObject node = new BpmnBaseObject
                        {
                            BizObject = new FormBpmnObject
                            {
                                Flowid = item.FlowId.ToString(),
                                Flowdesc = item.Flowdesc,
                                Flowtype = item.FlowType,
                                Flowname = item.Flowname,
                            },
                            bpmntype = item.FlowType,
                            id = item.FlowId.ToString(),
                        };
                        activity.DefinitionsDesc = ruleflow.RuleDesc;
                        activity.RuleId = ruleflow.RuleId;
                        activity.DefinitionsName = ruleflow.Name;
                        activity.DefinitionsStatus = ruleflow.RuleStatus ?? 0;

                        activity.BaseBpmnObjects ??= new List<BpmnBaseObject>();
                        activity.BaseBpmnObjects.Add(node);
                        break;
                }
            }
            return new ApiResult<IoTSharp.Models.Rule.Activity>(ApiCode.Success, "rule has been removed", activity);
        }

        //模拟处理

        [HttpPost("[action]")]
        public async Task<ApiResult<dynamic>> Active([FromBody] JObject form)
        {
            var profile = await this.GetUserProfile();
            var formdata = form.First.First;
            var extradata = form.First.Next;
            var obj = extradata.First.First.First.Value<JToken>();
            var __ruleid = obj.Value<string>();
            var ruleid = Guid.Parse(__ruleid);

            var d = formdata.Value<JToken>().ToObject(typeof(ExpandoObject));
            var testabizId = Guid.NewGuid().ToString(); //根据业务保存起来，用来查询执行事件和步骤

            var result = await _flowRuleProcessor.RunFlowRules(ruleid, d, Guid.Empty, EventType.TestPurpose, testabizId);

            result.ForEach(c =>
            {
                _context.FlowOperations.Attach(c);
                _context.SaveChanges();
            });



            return new ApiResult<dynamic>(ApiCode.Success, "test complete", result.OrderBy(c => c.Step).
                Where(c => c.BaseEvent.Bizid == testabizId).ToList()
                .GroupBy(c => c.Step).Select(c => new
                {
                    Step = c.Key,
                    Nodes = c
                }).ToList());
        }







        /// <summary>
        ///
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>

        [HttpPost("[action]")]
        public async Task<ApiResult<PagedData<BaseEventDto>>> FlowEvents([FromBody] EventParam m)
        {
            var profile = await this.GetUserProfile();
            Expression<Func<BaseEvent, bool>> condition = x => x.EventStaus > -1;
            if (!string.IsNullOrEmpty(m.Name))
            {
                condition = condition.And(x => x.EventName.Contains(m.Name));
            }

            if (m.CreatTime != null && m.CreatTime.Length == 2)
            {
                condition = condition.And(x => x.CreaterDateTime > m.CreatTime[0] && x.CreaterDateTime < m.CreatTime[1]);
            }

            //if (m.Creator!=null)
            //{
            //    condition = condition.And(x => x.Creator == m.Creator);
            //}


            var result = _context.BaseEvents.OrderByDescending(c => c.CreaterDateTime).Where(condition)
                .Skip((m.offset) * m.limit).Take(m.limit).Select(c => new BaseEventDto
                {
                    Name = c.FlowRule.Name,
                    Bizid = c.Bizid,
                    CreaterDateTime = c.CreaterDateTime,
                    Creator = c.Creator,
                    EventDesc = c.EventDesc,
                    EventId = c.EventId,
                    EventStaus = c.EventStaus,
                    EventName = c.EventName,
                    MataData = c.MataData,
                    RuleId = c.FlowRule.RuleId,
                    Type = c.Type
                }).ToList();



            foreach (var item in result)
            {
                item.CreatorName = await GetCreatorName(item);

            }
            return new ApiResult<PagedData<BaseEventDto>>(ApiCode.Success, "OK", new PagedData<BaseEventDto>
            {
                total = _context.BaseEvents.Count(condition),
                rows = result
            });
        }


        private async Task<string> GetCreatorName(BaseEventDto dto)
        {
            if (dto.Type == EventType.Normal)
            {
                return _context.Device.SingleOrDefault(c => c.Id == dto.Creator)?.Name;
            }
            else
            {
                return (await _userManager.FindByIdAsync(dto.Creator.ToString()))?.UserName;

            }
        }



        [HttpGet("[action]")]
        public ApiResult<dynamic> GetFlowOperstions(Guid eventId)
        {
            return new ApiResult<dynamic>(ApiCode.Success, "OK", _context.FlowOperations.Where(c => c.BaseEvent.EventId == eventId).ToList().OrderBy(c => c.Step).
              ToList()
                .GroupBy(c => c.Step).Select(c => new
                {
                    Step = c.Key,
                    Nodes = c
                }).ToList());
        }




        [HttpGet("[action]")]
        public ApiResult<dynamic> GetExecutors()
        {
            return new ApiResult<dynamic>(ApiCode.Success, "OK", _helper.GetTaskExecutorList().Select(c => new { label = c.Key, value = c.Value.FullName }).ToList());
        }


        [HttpPost("[action]")]
        public async Task<ApiResult<PagedData<RuleTaskExecutor>>> Executors(ExecutorParam m)
        {
            var profile = await this.GetUserProfile();
            Expression<Func<Data.RuleTaskExecutor, bool>> condition = x => x.ExecutorStatus > -1;
            return new ApiResult<PagedData<RuleTaskExecutor>>(ApiCode.Success, "OK", new PagedData<RuleTaskExecutor>
            {
                total = await _context.RuleTaskExecutors.CountAsync(condition),
                rows = await _context.RuleTaskExecutors.OrderByDescending(c => c.AddDateTime).Where(condition).Skip((m.offset) * m.limit).Take(m.limit).ToListAsync()
            });

        }







        [HttpGet("[action]")]
        public async Task<ApiResult<RuleTaskExecutor>> GetExecutor(Guid Id)
        {
            var executor = await _context.RuleTaskExecutors.SingleOrDefaultAsync(c => c.ExecutorId == Id);

            if (executor != null)
            {
                return new ApiResult<RuleTaskExecutor>(ApiCode.Success, "Ok", executor);

            }
            return new ApiResult<RuleTaskExecutor>(ApiCode.CantFindObject, "cant't find that object", null);

        }


        [HttpGet("[action]")]
        public async Task<ApiResult<bool>> DeleteExecutor(Guid Id)
        {
            var executor = await _context.RuleTaskExecutors.SingleOrDefaultAsync(c => c.ExecutorId == Id);

            if (executor != null)
            {


                executor.ExecutorStatus = -1;
                _context.RuleTaskExecutors.Update(executor);
                await _context.SaveChangesAsync();
                return new ApiResult<bool>(ApiCode.Success, "Ok", true);

            }
            return new ApiResult<bool>(ApiCode.CantFindObject, "cant't find that object", false);
        }


        [HttpPost("[action]")]
        public async Task<ApiResult<bool>> UpdateExecutor(RuleTaskExecutor m)
        {
            var executor = await _context.RuleTaskExecutors.SingleOrDefaultAsync(c => c.ExecutorId == m.ExecutorId);
            var profile = await this.GetUserProfile();
            if (executor != null)
            {

                //   executor.Creator = profile.Id;
                //    executor.MataData = m.MataData;
                executor.DefaultConfig = m.DefaultConfig;
                executor.ExecutorDesc = m.ExecutorDesc;
                executor.ExecutorName = m.ExecutorName;
                executor.TypeName = m.ExecutorName;
                executor.Path = m.Path;
                executor.Tag = m.Tag;
                executor.Path = m.Path;
                _context.RuleTaskExecutors.Update(executor);
                await _context.SaveChangesAsync();
                return new ApiResult<bool>(ApiCode.Success, "Ok", true);

            }
            return new ApiResult<bool>(ApiCode.CantFindObject, "cant't find that object", false);

        }

        [HttpPost("[action]")]
        public async Task<ApiResult<bool>> AddExecutor(RuleTaskExecutor m)
        {
            var profile = await this.GetUserProfile();
            var executor = new RuleTaskExecutor();
            executor.DefaultConfig = m.DefaultConfig;
            executor.ExecutorDesc = m.ExecutorDesc;
            executor.ExecutorName = m.ExecutorName;
            executor.TypeName = m.ExecutorName;
            executor.Path = m.Path;
            executor.Tag = m.Tag;
            executor.Path = m.Path;
            executor.AddDateTime = DateTime.Now;
            executor.Creator = profile.Id;
            executor.ExecutorStatus = 1;
            _context.RuleTaskExecutors.Add(executor);
            await _context.SaveChangesAsync();
            return new ApiResult<bool>(ApiCode.Success, "Ok", true);
        }






        [HttpPost("[action]")]
        public async Task<ApiResult<RuleTaskExecutorTestResultDto>> TestTask(RuleTaskExecutorTestDto m)
        {
            var profile = await this.GetUserProfile();

            var result = await this._flowRuleProcessor.TestScript(m.ruleId, m.flowId, m.Data);



            await _context.SaveChangesAsync();
            return new ApiResult<RuleTaskExecutorTestResultDto>(ApiCode.Success, "Ok", new RuleTaskExecutorTestResultDto() { Data = result.Data });
        }



        [HttpPost("RuleCondition")]
        public async Task<ApiResult<ConditionTestResult>> RuleCondition([FromBody] RuleTaskFlowTestResultDto m)
        {
            var profile = await this.GetUserProfile();
            var data = JsonConvert.DeserializeObject(m.Data) as JObject;
            var d = data.ToObject(typeof(ExpandoObject));
            var result = await this._flowRuleProcessor.TestCondition(m.ruleId, m.flowId, d);




            return new ApiResult<ConditionTestResult>(ApiCode.Success, "Ok", result);
        }





    }
}