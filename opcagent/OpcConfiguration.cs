
using Newtonsoft.Json;
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpcAgent
{
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using static OpcSession;
    using static OpcStackConfiguration;
    using static Program;

    public static class OpcConfiguration
    {
        public static SemaphoreSlim OpcConfigurationSemaphore { get; set; }
        public static List<OpcSession> OpcSessions { get; set; }
        public static SemaphoreSlim OpcSessionsListSemaphore { get; set; }
        public static SemaphoreSlim OpcActionListSemaphore { get; set; }

        public static string OpcActionConfigurationFilename { get; set; } = $"{System.IO.Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}actionconfig.json";

        public static int NumberOfOpcSessions
        {
            get
            {
                int result = 0;
                try
                {
                    OpcSessionsListSemaphore.Wait();
                    result = OpcSessions.Count();
                }
                finally
                {
                    OpcSessionsListSemaphore.Release();
                }
                return result;
            }
        }

        public static int NumberOfConnectedOpcSessions
        {
            get
            {
                int result = 0;
                try
                {
                    OpcSessionsListSemaphore.Wait();
                    result = OpcSessions.Count(s => s.State == OpcSession.SessionState.Connected);
                }
                finally
                {
                    OpcSessionsListSemaphore.Release();
                }
                return result;
            }
        }

        /// <summary>
        /// Initialize resources for the node configuration
        /// </summary>
        public static void Init()
        {
            OpcSessionsListSemaphore = new SemaphoreSlim(1);
            OpcConfigurationSemaphore = new SemaphoreSlim(1);
            OpcSessions = new List<OpcSession>();
            OpcActionListSemaphore = new SemaphoreSlim(1);
            OpcSessions = new List<OpcSession>();
            _actionConfiguration = new List<OpcActionConfigurationModel>();
        }

        /// <summary>
        /// Frees resources for the node configuration
        /// </summary>
        public static void Deinit()
        {
            OpcSessions = null;
            OpcSessionsListSemaphore.Dispose();
            OpcSessionsListSemaphore = null;
            OpcActionListSemaphore.Dispose();
            OpcActionListSemaphore = null;
        }

        /// <summary>
        /// Read and parse the startup configuration file
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> ReadOpcConfigurationAsync()
        {
            // get information on actions and validate the json by deserializing it
            try
            {
                await OpcActionListSemaphore.WaitAsync();
                Logger.Information($"The name of the action configuration file is: {OpcActionConfigurationFilename}");

                // if the file exists, read it, if not just continue 
                if (File.Exists(OpcActionConfigurationFilename))
                {
                    Logger.Information($"Attemtping to load action configuration from: {OpcActionConfigurationFilename}");
                    _actionConfiguration = JsonConvert.DeserializeObject<List<OpcActionConfigurationModel>>(File.ReadAllText(OpcActionConfigurationFilename));
                }
                else
                {
                    Logger.Information($"The action configuration file '{OpcActionConfigurationFilename}' does not exist. Continue and wait for remote configuration requests.");
                }
            }
            catch (Exception e)
            {
                Logger.Fatal(e, "Loading of the action configuration file failed. Does the file exist and has correct syntax? Exiting...");
                return false;
            }
            finally
            {
                OpcActionListSemaphore.Release();
            }
            Logger.Information($"There are {_actionConfiguration.Count.ToString()} actions.");
            return true;
        }

        /// <summary>
        /// Create specific well-known actions
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> CreateWellKnownConfigurationAsync()
        {
            // get information on actions and validate the json by deserializing it
            try
            {
                await OpcActionListSemaphore.WaitAsync();

                // connectivity test action
                if (TestConnectivity)
                {
                    Logger.Information($"Creating test action for connectivity test with security enabled.");
                    _actionConfiguration.Add(new OpcActionConfigurationModel(TestConnectivityUrl, true, new TestActionModel()));
                }

                // if the file exists, read it, if not just continue 
                if (File.Exists(OpcActionConfigurationFilename))
                {
                    Logger.Information($"Attemtping to load action configuration from: {OpcActionConfigurationFilename}");
                    _actionConfiguration = JsonConvert.DeserializeObject<List<OpcActionConfigurationModel>>(File.ReadAllText(OpcActionConfigurationFilename));
                }
                else
                {
                    Logger.Information($"The action configuration file '{OpcActionConfigurationFilename}' does not exist. Continue and wait for remote configuration requests.");
                }
            }
            catch (Exception e)
            {
                Logger.Fatal(e, "Loading of the action configuration file failed. Does the file exist and has correct syntax? Exiting...");
                return false;
            }
            finally
            {
                OpcActionListSemaphore.Release();
            }
            Logger.Information($"There are {_actionConfiguration.Count.ToString()} actions.");
            return true;
        }
        /// <summary>
        /// Create the data structures to manage OPC sessions and actions.
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> CreateOpcActionDataAsync()
        {
            try
            {
                await OpcActionListSemaphore.WaitAsync();
                await OpcSessionsListSemaphore.WaitAsync();

                // create actions out of the configuration
                var uniqueEndpointUrls = _actionConfiguration.Select(n => n.EndpointUrl).Distinct();
                foreach (var endpointUrl in uniqueEndpointUrls)
                {
                    // create new session info.
                    OpcSession opcSession = new OpcSession(endpointUrl, _actionConfiguration.Where(n => n.EndpointUrl == endpointUrl).First().UseSecurity, OpcSessionCreationTimeout);

                    // add all actions to the session
                    List<OpcAction> actionsOnEndpoint = new List<OpcAction>();
                    var endpointConfigs = _actionConfiguration.Where(c => c.EndpointUrl == endpointUrl);
                    foreach (var config in endpointConfigs)
                    {
                        config?.Read.ForEach(r => opcSession.OpcActions.Add(new OpcReadAction(r)));
                        config?.Write.ForEach(w => opcSession.OpcActions.Add(new OpcWriteAction(w)));
                    }

                    // add session.
                    OpcSessions.Add(opcSession);
                }
            }
            catch (Exception e)
            {
                Logger.Fatal(e, "Creation of the internal OPC management structures failed. Exiting...");
                return false;
            }
            finally
            {
                OpcSessionsListSemaphore.Release();
                OpcActionListSemaphore.Release();
            }
            return true;
        }

        private static List<OpcActionConfigurationModel> _actionConfiguration;
    }
}
