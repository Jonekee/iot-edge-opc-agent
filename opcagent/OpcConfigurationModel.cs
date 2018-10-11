
using Newtonsoft.Json;
using Opc.Ua;
using System;
using System.Collections.Generic;

namespace OpcAgent
{
    using System.ComponentModel;

    /// <summary>
    /// Class describing an action on an OPC UA server
    /// </summary>
    public class ActionModel
    {
        public ActionModel()
        {
            Id = null;
            Interval = 0;
        }

        public ActionModel(string id, ulong interval)
        {
            Id = id;
            Interval = interval;
        }
        
        // Id of the target node. Can be:
        // a NodeId ("ns=")
        // an ExpandedNodeId ("nsu=")
        public string Id;

        // if set action will recur with a period of Interval seconds, if set to 0 it will done only once
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling =DefaultValueHandling.IgnoreAndPopulate)]
        public ulong Interval;
    }

    /// <summary>
    /// Class describing a read action on an OPC UA server
    /// </summary>
    public class ReadActionModel : ActionModel
    {
        public ReadActionModel()
        {
            Id = null;
            Interval = 0;
        }

        public ReadActionModel(ReadActionModel action)
        {
            Id = action.Id;
            Interval = action.Interval;
        }
    }

    /// <summary>
    /// Class describing a test action on an OPC UA server
    /// </summary>
    public class TestActionModel : ActionModel
    {
        /// <summary>
        /// Default test works on the current time node with an interval of 30 sec.
        /// </summary>
        public TestActionModel()
        {
            Id = "i=2258";
            Interval = 30;
        }

        public TestActionModel(string id, ulong interval)
        {
            Id = id;
            Interval = interval;
        }

        public TestActionModel(TestActionModel action)
        {
            Id = action.Id;
            Interval = action.Interval;
        }
    }

    /// <summary>
    /// Class describing a write action on an OPC UA server
    /// </summary>
    public class WriteActionModel : ActionModel
    {
        public WriteActionModel()
        {
            Id = null;
            Interval = 0;
        }

        public WriteActionModel(WriteActionModel action)
        {
            Id = action.Id;
            Interval = action.Interval;
            Value = action.Value;
        }

        // value to write
        public dynamic Value;
    }

    /// <summary>
    /// Class describing the nodes which should be published.
    /// </summary>
    public partial class OpcActionConfigurationModel
    {
        public OpcActionConfigurationModel()
        {
        }

        public OpcActionConfigurationModel(string endpointUrl, bool useSecurity, ReadActionModel readAction)
        {
            EndpointUrl = new Uri(endpointUrl);
            UseSecurity = useSecurity;

            Read = new List<ReadActionModel>();
            Read.Add(new ReadActionModel(readAction));
        }

        public OpcActionConfigurationModel(string endpointUrl, bool useSecurity, TestActionModel testAction)
        {
            EndpointUrl = new Uri(endpointUrl);
            UseSecurity = useSecurity;

            Test = new List<TestActionModel>();
            Test.Add(new TestActionModel(testAction));
        }

        public OpcActionConfigurationModel(string endpointUrl, bool useSecurity, WriteActionModel writeAction)
        {
            EndpointUrl = new Uri(endpointUrl);
            UseSecurity = useSecurity;

            Write = new List<WriteActionModel>();
            Write.Add(new WriteActionModel(writeAction));
        }

        public Uri EndpointUrl { get; set; }

        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
        public bool UseSecurity { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<ReadActionModel> Read { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<WriteActionModel> Write { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<TestActionModel> Test { get; set; }
    }
}
