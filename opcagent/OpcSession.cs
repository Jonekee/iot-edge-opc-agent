
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
    public class OpcSession
    {
        public enum SessionState
        {
            Disconnected = 0,
            Connecting,
            Connected,
        }

        public static bool FetchOpcNodeDisplayName { get; set; } = false;

        public static int SessionConnectWaitSec { get; set; } = 10;

        public Uri EndpointUrl;

        public Session OpcUaClientSession;

        public SessionState State;

        public uint UnsuccessfulConnectionCount;

        public uint MissedKeepAlives;

        public int PublishingInterval;

        public uint SessionTimeout { get; }

        public bool UseSecurity { get; set; } = true;

        public AutoResetEvent ConnectAndMonitorSession;

        public List<OpcAction> OpcActions;

        /// <summary>
        /// Ctor for the session.
        /// </summary>
        public OpcSession(Uri endpointUrl, bool useSecurity, uint sessionTimeout)
        {
            State = SessionState.Disconnected;
            EndpointUrl = endpointUrl;
            SessionTimeout = sessionTimeout * 1000;
            UnsuccessfulConnectionCount = 0;
            MissedKeepAlives = 0;
            PublishingInterval = OpcPublishingInterval;
            UseSecurity = useSecurity;
            ConnectAndMonitorSession = new AutoResetEvent(false);
            _sessionCancelationTokenSource = new CancellationTokenSource();
            _sessionCancelationToken = _sessionCancelationTokenSource.Token;
            _opcSessionSemaphore = new SemaphoreSlim(1);
            _namespaceTable = new NamespaceTable();
            OpcActions = new List<OpcAction>();
            Task.Run(ConnectAndMonitorAsync);
        }

        /// <summary>
        /// This task is started when a session is configured and is running till session shutdown and ensures:
        /// - disconnected sessions are reconnected.
        /// - monitored nodes are no longer monitored if requested to do so.
        /// - monitoring for a node starts if it is required.
        /// - unused subscriptions (without any nodes to monitor) are removed.
        /// - sessions with out subscriptions are removed.
        /// </summary>
        public async Task ConnectAndMonitorAsync()
        {
            WaitHandle[] connectAndMonitorEvents = new WaitHandle[] 
            {
                _sessionCancelationToken.WaitHandle,
                ConnectAndMonitorSession
            };

            // run till session is closed
            while (!_sessionCancelationToken.IsCancellationRequested)
            {
                try
                {
                    // wait till:
                    // - cancelation is requested
                    // - got signaled because we need to check for pending session activity
                    // - timeout to try to reestablish any disconnected sessions
                    WaitHandle.WaitAny(connectAndMonitorEvents, SessionConnectWaitSec * 1000);

                    // step out on cancel
                    if (_sessionCancelationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await ConnectSessionAsync(_sessionCancelationToken);

                    await ExecuteCommandsAsync(_sessionCancelationToken);

                    await RemoveUnusedSessionsAsync(_sessionCancelationToken);
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Error in ConnectAndMonitorAsync.");
                }
            }
        }

        /// <summary>
        /// Connects the session if it is disconnected.
        /// </summary>
        public async Task ConnectSessionAsync(CancellationToken ct)
        {
            bool sessionLocked = false;
            try
            {
                EndpointDescription selectedEndpoint = null;
                ConfiguredEndpoint configuredEndpoint = null;
                sessionLocked = await LockSessionAsync();

                // if the session is already connected or connecting or shutdown in progress, return
                if (!sessionLocked || ct.IsCancellationRequested || State == SessionState.Connected || State == SessionState.Connecting)
                {
                    return;
                }

                Logger.Information($"Connect and monitor session and nodes on endpoint '{EndpointUrl.AbsoluteUri}'.");
                State = SessionState.Connecting;
                try
                {
                    // release the session to not block for high network timeouts.
                    ReleaseSession();
                    sessionLocked = false;

                    // start connecting
                    selectedEndpoint = CoreClientUtils.SelectEndpoint(EndpointUrl.AbsoluteUri, UseSecurity);
                    configuredEndpoint = new ConfiguredEndpoint(null, selectedEndpoint, EndpointConfiguration.Create(OpcApplicationConfiguration));
                    uint timeout = SessionTimeout * ((UnsuccessfulConnectionCount >= OpcSessionCreationBackoffMax) ? OpcSessionCreationBackoffMax : UnsuccessfulConnectionCount + 1);
                    Logger.Information($"Create {(UseSecurity ? "secured" : "unsecured")} session for endpoint URI '{EndpointUrl.AbsoluteUri}' with timeout of {timeout} ms.");
                    OpcUaClientSession = await Session.Create(
                            OpcApplicationConfiguration,
                            configuredEndpoint,
                            true,
                            false,
                            OpcApplicationConfiguration.ApplicationName,
                            timeout,
                            new UserIdentity(new AnonymousIdentityToken()),
                            null);
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Session creation to endpoint '{EndpointUrl.AbsoluteUri}' failed {++UnsuccessfulConnectionCount} time(s). Please verify if server is up and {ProgramName} configuration is correct.");
                    State = SessionState.Disconnected;
                    OpcUaClientSession = null;
                    return;
                }
                finally
                {
                    if (OpcUaClientSession != null)
                    {
                        sessionLocked = await LockSessionAsync();
                        if (sessionLocked)
                        {
                            Logger.Information($"Session successfully created with Id {OpcUaClientSession.SessionId}.");
                            if (!selectedEndpoint.EndpointUrl.Equals(configuredEndpoint.EndpointUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                            {
                                Logger.Information($"the Server has updated the EndpointUrl to '{selectedEndpoint.EndpointUrl}'");
                            }

                            // init object state and install keep alive
                            UnsuccessfulConnectionCount = 0;
                            OpcUaClientSession.KeepAliveInterval = OpcKeepAliveIntervalInSec * 1000;
                            OpcUaClientSession.KeepAlive += StandardClient_KeepAlive;

                            // fetch the namespace array and cache it. it will not change as long the session exists.
                            DataValue namespaceArrayNodeValue = OpcUaClientSession.ReadValue(VariableIds.Server_NamespaceArray);
                            _namespaceTable.Update(namespaceArrayNodeValue.GetValue<string[]>(null));

                            // show the available namespaces
                            Logger.Information($"The session to endpoint '{selectedEndpoint.EndpointUrl}' has {_namespaceTable.Count} entries in its namespace array:");
                            int i = 0;
                            foreach (var ns in _namespaceTable.ToArray())
                            {
                                Logger.Information($"Namespace index {i++}: {ns}");
                            }

                            // fetch the minimum supported item sampling interval from the server.
                            DataValue minSupportedSamplingInterval = OpcUaClientSession.ReadValue(VariableIds.Server_ServerCapabilities_MinSupportedSampleRate);
                            _minSupportedSamplingInterval = minSupportedSamplingInterval.GetValue(0);
                            Logger.Information($"The server on endpoint '{selectedEndpoint.EndpointUrl}' supports a minimal sampling interval of {_minSupportedSamplingInterval} ms.");
                            State = SessionState.Connected;
                        }
                        else
                        {
                            State = SessionState.Disconnected;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Error in ConnectSessions.");
            }
            finally
            {
                if (sessionLocked)
                {
                    ReleaseSession();
                }
            }
        }

        /// <summary>
        /// Executes OPC commands.
        /// </summary>
        public async Task ExecuteCommandsAsync(CancellationToken ct)
        {
            bool sessionLocked = false;
            try
            {
                sessionLocked = await LockSessionAsync();

                // if the session is not connected or shutdown in progress, return
                if (!sessionLocked || ct.IsCancellationRequested || State != SessionState.Connected)
                {
                    return;
                }

                Logger.Information($"Execute OPC commands on endpoint '{EndpointUrl.AbsoluteUri}'.");

                // check if there are any commands ready to execute
                var currentTicks = DateTime.UtcNow.Ticks;
                foreach (var action in OpcActions)
                {
                    if (action.NodeId == null)
                    {
                        try
                        {
                            // get the NodeId
                            NodeId nodeId = GetNodeIdFromId(action.Id);
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, $"Id of the action on endpoint '{EndpointUrl}' is invalid.");
                        }
                    }
                    switch (action)
                    {
                        case OpcReadAction readAction:
                            Logger.Information($"Start read action of node '{action.NodeId.Identifier}' on '{EndpointUrl}'");

                            // read the node info
                            Node node = OpcUaClientSession.ReadNode(action.NodeId);

                            // report the node info
                            Logger.Information($"Node Displayname is '{node.DisplayName}'");
                            Logger.Information($"Node Description is '{node.Description}'");

                            // read the value
                            DataValue dataValue = OpcUaClientSession.ReadValue(action.NodeId);

                            // report the node value
                            Logger.Information($"Node Value is '{dataValue.Value}'");
                            Logger.Information($"Node Value is '{dataValue.ToString()}'");

                            break;

                    }
                }

            }
            catch (Exception e)
            {
                Logger.Error(e, "Error in executing actions on session.");
            }
            finally
            {
                if (sessionLocked)
                {
                    ReleaseSession();
                }
            }
        }

        private NodeId GetNodeIdFromId(string id)
        {
            NodeId nodeId = null;
            ExpandedNodeId expandedNodeId = null;
            try
            {
                if (id.Contains("nsu="))
                {
                    expandedNodeId = ExpandedNodeId.Parse(id);
                    nodeId = new NodeId(expandedNodeId.Identifier, (ushort)_namespaceTable.GetIndex(expandedNodeId.NamespaceUri));

                }
                else
                {
                    nodeId = NodeId.Parse(id);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"The NodeId has an invalid format '{id}'!");
            }
            return nodeId;
        }

        /// <summary>
        /// Checks if there are session without any subscriptions and remove them.
        /// </summary>
        public async Task RemoveUnusedSessionsAsync(CancellationToken ct)
        {
            try
            {
                await OpcSessionsListSemaphore.WaitAsync();

                // if session is not connected or shutdown is in progress, return
                if (ct.IsCancellationRequested || State != SessionState.Connected)
                {
                    return;
                }

                // remove sessions in the stack
                var sessionsToRemove = new List<OpcSession>();
                foreach (var sessionToRemove in sessionsToRemove)
                {
                    Logger.Information($"Remove unused session on endpoint '{EndpointUrl}'.");
                    await sessionToRemove.ShutdownAsync();
                }
            }
            finally
            {
                OpcSessionsListSemaphore.Release();
            }
        }

        /// <summary>
        /// Disconnects a session and removes all subscriptions on it and marks all nodes on those subscriptions
        /// as unmonitored.
        /// </summary>
        public async Task DisconnectAsync()
        {
            bool sessionLocked = await LockSessionAsync();
            if (sessionLocked)
            {
                try
                {
                    InternalDisconnect();
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Error while disconnecting '{EndpointUrl}'.");
                }
                ReleaseSession();
            }
        }

        /// <summary>
        /// Returns the namespace index for a namespace URI.
        /// </summary>
        public int GetNamespaceIndexUnlocked(string namespaceUri)
        {
            return _namespaceTable.GetIndex(namespaceUri);
        }

        /// <summary>
        /// Internal disconnect method. Caller must have taken the _opcSessionSemaphore.
        /// </summary>
        private void InternalDisconnect()
        {
            try
            {
                try
                {
                    OpcUaClientSession.Close();
                }
                catch
                {
                    // the session might be already invalidated. ignore.
                }
                OpcUaClientSession = null;
            }
            catch (Exception e)
            {
                Logger.Error(e, "Error in InternalDisconnect.");
            }
            State = SessionState.Disconnected;
            MissedKeepAlives = 0;
        }

    /// <summary>
    /// Shutdown the current session if it is connected.
    /// </summary>
    public async Task ShutdownAsync()
        {
            bool sessionLocked = false;
            try
            {
                sessionLocked = await LockSessionAsync();

                // if the session is connected, close it.
                if (sessionLocked && (State == SessionState.Connecting || State == SessionState.Connected))
                {
                    try
                    {
                        Logger.Information($"Closing session to endpoint URI '{EndpointUrl.AbsoluteUri}' closed successfully.");
                        OpcUaClientSession.Close();
                        State = SessionState.Disconnected;
                        Logger.Information($"Session to endpoint URI '{EndpointUrl.AbsoluteUri}' closed successfully.");
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, $"Error while closing session to endpoint '{EndpointUrl.AbsoluteUri}'.");
                        State = SessionState.Disconnected;
                        return;
                    }
                }
            }
            finally
            {
                if (sessionLocked)
                {
                    // cancel all threads waiting on the session semaphore
                    _sessionCancelationTokenSource.Cancel();
                    _opcSessionSemaphore.Dispose();
                    _opcSessionSemaphore = null;
                }
            }
        }

        /// <summary>
        /// Create a subscription in the session.
        /// </summary>
        private Subscription CreateSubscription(int requestedPublishingInterval, out int revisedPublishingInterval)
        {
            Subscription subscription = new Subscription()
            {
                PublishingInterval = requestedPublishingInterval,
            };
            // need to happen before the create to set the Session property.
            OpcUaClientSession.AddSubscription(subscription);
            subscription.Create();
            Logger.Information($"Created subscription with id {subscription.Id} on endpoint '{EndpointUrl.AbsoluteUri}'");
            if (requestedPublishingInterval != subscription.PublishingInterval)
            {
                Logger.Information($"Publishing interval: requested: {requestedPublishingInterval}; revised: {subscription.PublishingInterval}");
            }
            revisedPublishingInterval = subscription.PublishingInterval;
            return subscription;
        }

        /// <summary>
        /// Handler for the standard "keep alive" event sent by all OPC UA servers
        /// </summary>
        private void StandardClient_KeepAlive(Session session, KeepAliveEventArgs e)
        {
            // Ignore if we are shutting down.
            if (ShutdownTokenSource.IsCancellationRequested == true)
            {
                return;
            }

            if (e != null && session != null && session.ConfiguredEndpoint != null && OpcUaClientSession != null)
            {
                try
                {
                    if (!ServiceResult.IsGood(e.Status))
                    {
                        Logger.Warning($"Session endpoint: {session.ConfiguredEndpoint.EndpointUrl} has Status: {e.Status}");
                        Logger.Information($"Outstanding requests: {session.OutstandingRequestCount}, Defunct requests: {session.DefunctRequestCount}");
                        Logger.Information($"Good publish requests: {session.GoodPublishRequestCount}, KeepAlive interval: {session.KeepAliveInterval}");
                        Logger.Information($"SessionId: {session.SessionId}");

                        if (State == SessionState.Connected)
                        {
                            MissedKeepAlives++;
                            Logger.Information($"Missed KeepAlives: {MissedKeepAlives}");
                            if (MissedKeepAlives >= OpcKeepAliveDisconnectThreshold)
                            {
                                Logger.Warning($"Hit configured missed keep alive threshold of {OpcKeepAliveDisconnectThreshold}. Disconnecting the session to endpoint {session.ConfiguredEndpoint.EndpointUrl}.");
                                session.KeepAlive -= StandardClient_KeepAlive;
                                Task t = Task.Run(async () => await DisconnectAsync());
                            }
                        }
                    }
                    else
                    {
                        if (MissedKeepAlives != 0)
                        {
                            // Reset missed keep alive count
                            Logger.Information($"Session endpoint: {session.ConfiguredEndpoint.EndpointUrl} got a keep alive after {MissedKeepAlives} {(MissedKeepAlives == 1 ? "was" : "were")} missed.");
                            MissedKeepAlives = 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error in keep alive handling for endpoint '{session.ConfiguredEndpoint.EndpointUrl}'. (message: '{ex.Message}'");
                }
            }
            else
            {
                Logger.Warning("Keep alive arguments seems to be wrong.");
            }
        }

        /// <summary>
        /// Take the session semaphore.
        /// </summary>
        public async Task<bool> LockSessionAsync()
        {
            await _opcSessionSemaphore.WaitAsync(_sessionCancelationToken);
            if (_sessionCancelationToken.IsCancellationRequested)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Release the session semaphore.
        /// </summary>
        public void ReleaseSession()
        {
            _opcSessionSemaphore.Release();
        }

        private SemaphoreSlim _opcSessionSemaphore;
        private CancellationTokenSource _sessionCancelationTokenSource;
        private CancellationToken _sessionCancelationToken;
        private NamespaceTable _namespaceTable;
        private double _minSupportedSamplingInterval;
    }
}
