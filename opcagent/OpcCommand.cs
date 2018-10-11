
using Opc.Ua.Client;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpcAgent
{
    using Opc.Ua;
    using System.Diagnostics;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using static OpcStackConfiguration;
    using static OpcConfiguration;
    using static Program;

    /// <summary>
    /// Class to manage OPC commands.
    /// </summary>
    public class OpcCommand
    {
        public DateTime NextExecution;
        public bool Recurring;
        //public OpcCommandResultChannel ResultChannel;

        public OpcCommand(bool recurring = false)
        {
            NextExecution = DateTime.UtcNow;
            Recurring = recurring;
        }
    }

    public class OpcReadCommand : OpcCommand
    {
        public string NodeId;
    }
    public class OpcWriteCommand : OpcCommand
    {
        public string NodeId;
        public string Value;
    }
}
