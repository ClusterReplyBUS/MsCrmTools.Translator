using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace MsCrmTools.Translator.AppCode
{
    public enum LogType
    {
        Info,
        Warning,
        Error
    }

    public class BaseTranslation
    {
        private const int MAX_REQUESTS = 5;
        protected string name;
        private Queue<ExecuteMultipleRequest> requestsQueue;
        private ExecuteMultipleRequest latestRequest;

        public event EventHandler<LogEventArgs> Log;

        public event EventHandler<TranslationProgressEventArgs> Result;

        public virtual void OnLog(LogEventArgs e)
        {
            Log?.Invoke(this, e);
        }

        public virtual void OnResult(TranslationProgressEventArgs e)
        {
            Result?.Invoke(this, e);
        }

        protected void AddRequest(OrganizationRequest or)
        {
            if (requestsQueue == null)
                requestsQueue = new Queue<ExecuteMultipleRequest>();
            if (latestRequest == null)
                latestRequest = new ExecuteMultipleRequest
                {
                    Requests = new OrganizationRequestCollection(),
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = true,
                        ReturnResponses = false
                    }
                };
            latestRequest.Requests.Add(or);
            if (latestRequest.Requests.Count >= MAX_REQUESTS)
            {
                requestsQueue.Enqueue(latestRequest);
                latestRequest = new ExecuteMultipleRequest
                {
                    Requests = new OrganizationRequestCollection(),
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = true,
                        ReturnResponses = false
                    }
                };
            }
        }

        protected void ExecuteMultiple(IOrganizationService service, TranslationProgressEventArgs e, bool forceUpdate /*= false*/)
        {
            if (requestsQueue == null)
                return;
            requestsQueue.Enqueue(latestRequest);
            var list = requestsQueue.ToList();
            while (requestsQueue.Count > 0)
            {
                var request = requestsQueue.Dequeue();
                //if (request.Requests.Count % 1000 != 0 && forceUpdate == false) return;

                e.SheetName = name;
                e.TotalItems += request.Requests.Count;

                OnResult(e);
                try
                {
                    var bulkResponse = (ExecuteMultipleResponse)service.Execute(request);

                    if (bulkResponse.IsFaulted)
                    {
                        e.FailureCount += bulkResponse.Responses.Count(r => r.Fault != null);
                        e.SuccessCount += request.Requests.Count - bulkResponse.Responses.Count;

                        foreach (var response in bulkResponse.Responses)
                        {
                            if (response.Fault != null)
                            {
                                string message;
                                var faultIndex = response.RequestIndex;
                                var faultRequest = request.Requests[faultIndex];

                                if (faultRequest is UpdateRequest ur)
                                {
                                    message =
                                        $"Error while updating record {ur.Target.LogicalName} ({ur.Target.Id}): {response.Fault.Message}";
                                }
                                else if (faultRequest is UpdateAttributeRequest uar)
                                {
                                    message =
                                        $"Error while updating attribute {uar.Attribute.LogicalName}: {response.Fault.Message}";
                                }
                                else if (faultRequest is UpdateRelationshipRequest urr)
                                {
                                    message =
                                        $"Error while updating relationship {urr.Relationship.SchemaName}: {response.Fault.Message}";
                                }
                                else if (faultRequest is UpdateOptionSetRequest uosr)
                                {
                                    message =
                                        $"Error while updating optionset {uosr.OptionSet.Name}: {response.Fault.Message}";
                                }
                                else if (faultRequest is UpdateOptionValueRequest uovr)
                                {
                                    if (!string.IsNullOrEmpty(uovr.OptionSetName))
                                    {
                                        message =
                                            $"Error while updating global optionset ({uovr.OptionSetName}) value ({uovr.Value}) label: {response.Fault.Message}";
                                    }
                                    else
                                    {
                                        message =
                                            $"Error while updating option ({uovr.Value}) label for attribute {uovr.AttributeLogicalName} ({uovr.EntityLogicalName}): {response.Fault.Message}";
                                    }
                                }
                                else if (faultRequest is SetLocLabelsRequest sllr)
                                {
                                    message =
                                        $"Error while updating {sllr.AttributeName} of record {sllr.EntityMoniker.LogicalName} ({sllr.EntityMoniker.Id}): {response.Fault.Message}";
                                }
                                else
                                {
                                    message = response.Fault.Message;
                                }

                                OnLog(new LogEventArgs(message) { Type = LogType.Error });
                            }
                        }
                    }
                    else
                    {
                        e.SuccessCount += request.Requests.Count;
                    }

                    OnResult(e);
                }
                catch (Exception ex)
                {
                    e.FailureCount += request.Requests.Count;
                    foreach (var faultRequest in request.Requests)
                    {
                        string message;

                        if (faultRequest is UpdateRequest ur)
                        {
                            message =
                                $"Error while updating record {ur.Target.LogicalName} ({ur.Target.Id}): {ex}";
                        }
                        else if (faultRequest is UpdateAttributeRequest uar)
                        {
                            message =
                                $"Error while updating attribute {uar.Attribute.LogicalName}: {ex}";
                        }
                        else if (faultRequest is UpdateRelationshipRequest urr)
                        {
                            message =
                                $"Error while updating relationship {urr.Relationship.SchemaName}: {ex}";
                        }
                        else if (faultRequest is UpdateOptionSetRequest uosr)
                        {
                            message =
                                $"Error while updating optionset {uosr.OptionSet.Name}: {ex}";
                        }
                        else if (faultRequest is UpdateOptionValueRequest uovr)
                        {
                            if (!string.IsNullOrEmpty(uovr.OptionSetName))
                            {
                                message =
                                    $"Error while updating global optionset ({uovr.OptionSetName}) value ({uovr.Value}) label: {ex}";
                            }
                            else
                            {
                                message =
                                    $"Error while updating option ({uovr.Value}) label for attribute {uovr.AttributeLogicalName} ({uovr.EntityLogicalName}): {ex}";
                            }
                        }
                        else if (faultRequest is SetLocLabelsRequest sllr)
                        {
                            message =
                                $"Error while updating {sllr.AttributeName} of record {sllr.EntityMoniker.LogicalName} ({sllr.EntityMoniker.Id}): {ex}";
                        }
                        else
                        {
                            message = ex.ToString();
                        }

                        OnLog(new LogEventArgs(message) { Type = LogType.Error });
                    }
                }
                //InitMultipleRequest();
            }
        }
        /*
                protected void InitMultipleRequest()
                {

                    latestRequest = new ExecuteMultipleRequest
                    {
                        Requests = new OrganizationRequestCollection(),
                        Settings = new ExecuteMultipleSettings
                        {
                            ContinueOnError = true,
                            ReturnResponses = false
                        }
                    };
                    requests.Enqueue(latestRequest);
                }*/
    }

    public class LogEventArgs : EventArgs
    {
        public LogEventArgs()
        {
        }

        public LogEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; set; }
        public LogType Type { get; set; }
    }

    public class TranslationProgressEventArgs : EventArgs
    {
        public int FailureCount { get; set; }
        public string SheetName { get; set; }
        public int SuccessCount { get; set; }
        public int TotalItems { get; set; }
    }

    public class TranslationResultEventArgs : EventArgs
    {
        public string Message { get; set; }
        public string SheetName { get; set; }
        public bool Success { get; set; }
    }
}