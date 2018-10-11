
using Opc.Ua.Client;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpcAgent
{
    using Opc.Ua;
    using System.Threading;
    using System.Threading.Tasks;
    using static OpcConfiguration;
    using static OpcStackConfiguration;
    using static Program;

    /// <summary>
    /// Class to manage OPC sessions.
    /// </summary>
    public class OpcAction
    {
        public string Id;

        public ulong Interval;

        public long NextExecution;

        public NodeId NodeId;
               
        /// <summary>
        /// Ctor for the action.
        /// </summary>
        public OpcAction(string id, ulong interval)
        {
            Interval = interval;
            Id = id;
            NextExecution = DateTime.UtcNow.Ticks;
            NodeId = null;
        }
    }

    public class OpcReadAction : OpcAction
    {
        /// <summary>
        /// Ctor for the read action.
        /// </summary>
        public OpcReadAction(ReadActionModel action) : base(action.Id, action.Interval)
        {
        }
    }
    public class OpcWriteAction : OpcAction
    {
        public dynamic Value;

        /// <summary>
        /// Ctor for the write action.
        /// </summary>
        public OpcWriteAction(WriteActionModel action) : base(action.Id, action.Interval)
        {
            Value = action.Value; 
        }

    }
}
