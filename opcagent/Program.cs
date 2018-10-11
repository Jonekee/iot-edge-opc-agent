
using Mono.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace OpcAgent
{
    using Opc.Ua;
    using Opc.Ua.Server;
    using Serilog;
    using System.IO;
    using System.Text;
    using static Diagnostics;
    using static OpcSession;
    using static OpcStackConfiguration;
    using static OpcConfiguration;
    using static System.Console;

    public class Program
    {
        public const string ProgramName = "OpcAgent";

        public static CancellationTokenSource ShutdownTokenSource;

        public static uint ShutdownWaitPeriod { get; } = 10;

        public static Serilog.Core.Logger Logger { get; set; } = null;

        public static bool TestConnectivity { get; set; } = true;

        public static string TestConnectivityUrl { get; set; } = "opc.tcp://opcplc:50000";

        public static DateTime ProgramStartTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Synchronous main method of the app.
        /// </summary>
        public static void Main(string[] args)
        {
            // enable this to catch when running in IoTEdge
            //bool waitHere = true;
            //int i = 0;
            //while (waitHere)
            //{
            //    WriteLine($"forever loop (iteration {i++})");
            //    Thread.Sleep(5000);
            //}
            MainAsync(args).Wait();
        }

        /// <summary>
        /// Asynchronous part of the main method of the app.
        /// </summary>
        public async static Task MainAsync(string[] args)
        {
            try
            {
                var shouldShowHelp = false;

                // Shutdown token sources.
                ShutdownTokenSource = new CancellationTokenSource();

                // command line options
                Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                        { "cf|configfile=", $"the filename containing action configuration.\nDefault: '{OpcActionConfigurationFilename}'", (string p) => OpcActionConfigurationFilename = p },

                        { "tc|testconnectivity", $"URL of the OPC UA server used to test connectivity with.\nDefault: {TestConnectivity}", b => TestConnectivity = b != null },

                        { "tu|testurl=", $"URL of the OPC UA server used for tests.\nDefault: {TestConnectivityUrl}", (string s) => TestConnectivityUrl = s },


                        { "sw|sessionconnectwait=", $"specify the wait time in seconds we try to connect to disconnected endpoints and starts monitoring unmonitored items\nMin: 10\nDefault: {SessionConnectWaitSec}", (int i) => {
                                if (i > 10)
                                {
                                    SessionConnectWaitSec = i;
                                }
                                else
                                {
                                    throw new OptionException("The sessionconnectwait must be greater than 10 sec", "sessionconnectwait");
                                }
                            }
                        },
                        { "di|diagnosticsinterval=", $"shows diagnostic info at the specified interval in seconds (need log level info). 0 disables diagnostic output.\nDefault: {DiagnosticsInterval}", (uint u) => DiagnosticsInterval = u },

                        { "lf|logfile=", $"the filename of the logfile to use.\nDefault: './{_logFileName}'", (string l) => _logFileName = l },
                        { "lt|logflushtimespan=", $"the timespan in seconds when the logfile should be flushed.\nDefault: {_logFileFlushTimeSpanSec} sec", (int s) => {
                                if (s > 0)
                                {
                                    _logFileFlushTimeSpanSec = TimeSpan.FromSeconds(s);
                                }
                                else
                                {
                                    throw new Mono.Options.OptionException("The logflushtimespan must be a positive number.", "logflushtimespan");
                                }
                            }
                        },
                        { "ll|loglevel=", $"the loglevel to use (allowed: fatal, error, warn, info, debug, verbose).\nDefault: info", (string l) => {
                                List<string> logLevels = new List<string> {"fatal", "error", "warn", "info", "debug", "verbose"};
                                if (logLevels.Contains(l.ToLowerInvariant()))
                                {
                                    _logLevel = l.ToLowerInvariant();
                                }
                                else
                                {
                                    throw new Mono.Options.OptionException("The loglevel must be one of: fatal, error, warn, info, debug, verbose", "loglevel");
                                }
                            }
                        },
                        // opc configuration options
                        { "ol|opcmaxstringlen=", $"the max length of a string opc can transmit/receive.\nDefault: {OpcMaxStringLength}", (int i) => {
                                if (i > 0)
                                {
                                    OpcMaxStringLength = i;
                                }
                                else
                                {
                                    throw new OptionException("The max opc string length must be larger than 0.", "opcmaxstringlen");
                                }
                            }
                        },
                        { "ot|operationtimeout=", $"the operation timeout of the OPC UA client in ms.\nDefault: {OpcOperationTimeout}", (int i) => {
                                if (i >= 0)
                                {
                                    OpcOperationTimeout = i;
                                }
                                else
                                {
                                    throw new OptionException("The operation timeout must be larger or equal 0.", "operationtimeout");
                                }
                            }
                        },
                        { "ct|createsessiontimeout=", $"specify the timeout in seconds used when creating a session to an endpoint. On unsuccessful connection attemps a backoff up to {OpcSessionCreationBackoffMax} times the specified timeout value is used.\nMin: 1\nDefault: {OpcSessionCreationTimeout}", (uint u) => {
                                if (u > 1)
                                {
                                    OpcSessionCreationTimeout = u;
                                }
                                else
                                {
                                    throw new OptionException("The createsessiontimeout must be greater than 1 sec", "createsessiontimeout");
                                }
                            }
                        },
                        { "ki|keepaliveinterval=", $"specify the interval in seconds se send keep alive messages to the OPC servers on the endpoints it is connected to.\nMin: 2\nDefault: {OpcKeepAliveIntervalInSec}", (int i) => {
                                if (i >= 2)
                                {
                                    OpcKeepAliveIntervalInSec = i;
                                }
                                else
                                {
                                    throw new OptionException("The keepaliveinterval must be greater or equal 2", "keepalivethreshold");
                                }
                            }
                        },
                        { "kt|keepalivethreshold=", $"specify the number of keep alive packets a server can miss, before the session is disconneced\nMin: 1\nDefault: {OpcKeepAliveDisconnectThreshold}", (uint u) => {
                                if (u > 1)
                                {
                                    OpcKeepAliveDisconnectThreshold = u;
                                }
                                else
                                {
                                    throw new OptionException("The keepalivethreshold must be greater than 1", "keepalivethreshold");
                                }
                            }
                        },

                        { "aa|autoaccept", $"trusts all servers we establish a connection to.\nDefault: {OpcAutoTrustServerCerts}", b => OpcAutoTrustServerCerts = b != null },

                        // trust own public cert option
                        { "to|trustowncert", $"our own certificate is put into the trusted certificate store automatically.\nDefault: {TrustMyself}", t => TrustMyself = t != null  },

                        // own cert store options
                        { "at|appcertstoretype=", $"the own application cert store type. \n(allowed values: Directory, X509Store)\nDefault: '{OpcOwnCertStoreType}'", (string s) => {
                                if (s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) || s.Equals(CertificateStoreType.Directory, StringComparison.OrdinalIgnoreCase))
                                {
                                    OpcOwnCertStoreType = s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) ? CertificateStoreType.X509Store : CertificateStoreType.Directory;
                                    OpcOwnCertStorePath = s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) ? OpcOwnCertX509StorePathDefault : OpcOwnCertDirectoryStorePathDefault;
                                }
                                else
                                {
                                    throw new OptionException();
                                }
                            }
                        },
                        { "ap|appcertstorepath=", $"the path where the own application cert should be stored\nDefault (depends on store type):\n" +
                                $"X509Store: '{OpcOwnCertX509StorePathDefault}'\n" +
                                $"Directory: '{OpcOwnCertDirectoryStorePathDefault}'", (string s) => OpcOwnCertStorePath = s
                        },

                        // trusted cert store options
                        {
                        "tt|trustedcertstoretype=", $"the trusted cert store type. \n(allowed values: Directory, X509Store)\nDefault: {OpcTrustedCertStoreType}", (string s) => {
                                if (s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) || s.Equals(CertificateStoreType.Directory, StringComparison.OrdinalIgnoreCase))
                                {
                                    OpcTrustedCertStoreType = s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) ? CertificateStoreType.X509Store : CertificateStoreType.Directory;
                                    OpcTrustedCertStorePath = s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) ? OpcTrustedCertX509StorePathDefault : OpcTrustedCertDirectoryStorePathDefault;
                                }
                                else
                                {
                                    throw new OptionException();
                                }
                            }
                        },
                        { "tp|trustedcertstorepath=", $"the path of the trusted cert store\nDefault (depends on store type):\n" +
                                $"X509Store: '{OpcTrustedCertX509StorePathDefault}'\n" +
                                $"Directory: '{OpcTrustedCertDirectoryStorePathDefault}'", (string s) => OpcTrustedCertStorePath = s
                        },

                        // rejected cert store options
                        { "rt|rejectedcertstoretype=", $"the rejected cert store type. \n(allowed values: Directory, X509Store)\nDefault: {OpcRejectedCertStoreType}", (string s) => {
                                if (s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) || s.Equals(CertificateStoreType.Directory, StringComparison.OrdinalIgnoreCase))
                                {
                                    OpcRejectedCertStoreType = s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) ? CertificateStoreType.X509Store : CertificateStoreType.Directory;
                                    OpcRejectedCertStorePath = s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) ? OpcRejectedCertX509StorePathDefault : OpcRejectedCertDirectoryStorePathDefault;
                                }
                                else
                                {
                                    throw new OptionException();
                                }
                            }
                        },
                        { "rp|rejectedcertstorepath=", $"the path of the rejected cert store\nDefault (depends on store type):\n" +
                                $"X509Store: '{OpcRejectedCertX509StorePathDefault}'\n" +
                                $"Directory: '{OpcRejectedCertDirectoryStorePathDefault}'", (string s) => OpcRejectedCertStorePath = s
                        },

                        // issuer cert store options
                        {
                        "it|issuercertstoretype=", $"the trusted issuer cert store type. \n(allowed values: Directory, X509Store)\nDefault: {OpcIssuerCertStoreType}", (string s) => {
                                if (s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) || s.Equals(CertificateStoreType.Directory, StringComparison.OrdinalIgnoreCase))
                                {
                                    OpcIssuerCertStoreType = s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) ? CertificateStoreType.X509Store : CertificateStoreType.Directory;
                                    OpcIssuerCertStorePath = s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) ? OpcIssuerCertX509StorePathDefault : OpcIssuerCertDirectoryStorePathDefault;
                                }
                                else
                                {
                                    throw new OptionException();
                                }
                            }
                        },
                        { "ip|issuercertstorepath=", $"the path of the trusted issuer cert store\nDefault (depends on store type):\n" +
                                $"X509Store: '{OpcIssuerCertX509StorePathDefault}'\n" +
                                $"Directory: '{OpcIssuerCertDirectoryStorePathDefault}'", (string s) => OpcIssuerCertStorePath = s
                        },

                        // misc
                        { "h|help", "show this message and exit", h => shouldShowHelp = h != null },
                    };


                List<string> extraArgs = new List<string>();
                try
                {
                    // parse the command line
                    extraArgs = options.Parse(args);
                }
                catch (OptionException e)
                {
                    // initialize logging
                    InitLogging();

                    // show message
                    Logger.Error(e, "Error in command line options");
                    Logger.Error($"Command line arguments: {String.Join(" ", args)}");

                    // show usage
                    Usage(options);
                    return;
                }

                // initialize logging
                InitLogging();

                // show usage if requested
                if (shouldShowHelp)
                {
                    Usage(options);
                    return;
                }

                // Validate and parse extra arguments.
                if (extraArgs.Count > 1)
                {
                    Logger.Error("Error in command line options");
                    Logger.Error($"Command line arguments: {String.Join(" ", args)}");
                    Usage(options);
                    return;
                }

                // start operation
                Logger.Information($"{ProgramName} is starting up...");

                // allow canceling the application
                var quitEvent = new ManualResetEvent(false);
                try
                {
                    Console.CancelKeyPress += (sender, eArgs) =>
                    {
                        quitEvent.Set();
                        eArgs.Cancel = true;
                        ShutdownTokenSource.Cancel();
                    };
                }
                catch
                {
                }

                // init OPC configuration and tracing
                OpcStackConfiguration opcStackConfiguration = new OpcStackConfiguration();
                await opcStackConfiguration.ConfigureAsync();

                // Set certificate validator.
                if (OpcAutoTrustServerCerts)
                {
                    Logger.Information($"{ProgramName} configured to auto trust server certificates of the servers it is connecting to.");
                    OpcApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_AutoTrustServerCerts);
                }
                else
                {
                    Logger.Information($"{ProgramName} configured to not auto trust server certificates. When connecting to servers, you need to manually copy the rejected server certs to the trusted store to trust them.");
                    OpcApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_Default);
                }

                // read OPC action configuration
                OpcConfiguration.Init();
                if (!await ReadOpcConfigurationAsync())
                {
                    return;
                }

                // create OPC action data
                if (!await CreateOpcActionDataAsync())
                {
                    return;
                }

                // kick off OPC client activities
                await SessionStartAsync();

                // initialize diagnostics
                Diagnostics.Init();

                // stop on user request
                Logger.Information("");
                Logger.Information("");
                Logger.Information($"{ProgramName} is running. Press CTRL-C to quit.");

                // wait for Ctrl-C
                quitEvent.WaitOne(Timeout.Infinite);

                Logger.Information("");
                Logger.Information("");
                ShutdownTokenSource.Cancel();
                Logger.Information($"{ProgramName} is shutting down...");

                // shutdown all OPC sessions
                await SessionShutdownAsync();

                // shutdown diagnostics
                await ShutdownAsync();

                ShutdownTokenSource = null;
            }
            catch (Exception e)
            {
                Logger.Fatal(e, e.StackTrace);
                e = e.InnerException ?? null;
                while (e != null)
                {
                    Logger.Fatal(e, e.StackTrace);
                    e = e.InnerException ?? null;
                }
                Logger.Fatal($"{ProgramName} exiting... ");
            }
        }

        /// <summary>
        /// Start all sessions.
        /// </summary>
        public async static Task SessionStartAsync()
        {
            try
            {
                await OpcSessionsListSemaphore.WaitAsync();
                OpcSessions.ForEach(s => s.ConnectAndMonitorSession.Set());
            }
            catch (Exception e)
            {
                Logger.Fatal(e, "Failed to start all sessions.");
            }
            finally
            {
                OpcSessionsListSemaphore.Release();
            }
        }

        /// <summary>
        /// Shutdown all sessions.
        /// </summary>
        public async static Task SessionShutdownAsync()
        {
            try
            {
                while (OpcSessions.Count > 0)
                {
                    OpcSession opcSession = null;
                    try
                    {
                        await OpcSessionsListSemaphore.WaitAsync();
                        opcSession = OpcSessions.ElementAt(0);
                        OpcSessions.RemoveAt(0);
                    }
                    finally
                    {
                        OpcSessionsListSemaphore.Release();
                    }
                    await opcSession?.ShutdownAsync();
                }
            }
            catch (Exception e)
            {
                Logger.Fatal(e, "Failed to shutdown all sessions.");
            }

            // Wait and continue after a while.
            uint maxTries = ShutdownWaitPeriod;
            while (true)
            {
                int sessionCount = OpcSessions.Count;
                if (sessionCount == 0)
                {
                    return;
                }
                if (maxTries-- == 0)
                {
                    Logger.Information($"There are still {sessionCount} sessions alive. Ignore and continue shutdown.");
                    return;
                }
                Logger.Information($"{ProgramName} is shutting down. Wait {SessionConnectWaitSec} seconds, since there are stil {sessionCount} sessions alive...");
                await Task.Delay(SessionConnectWaitSec * 1000);
            }
        }

        /// <summary>
        /// Usage message.
        /// </summary>
        private static void Usage(Mono.Options.OptionSet options)
        {

            // show usage
            Logger.Information("");
            Logger.Information("Usage: {0}.exe [<options>]", Assembly.GetEntryAssembly().GetName().Name);
            Logger.Information("");
            Logger.Information($"{ProgramName} to run test operations against OPC UA servers.");
            Logger.Information("To exit the application, just press CTRL-C while it is running.");
            Logger.Information("");
            Logger.Information("There are a couple of environment variables which can be used to control the application:");
            Logger.Information("_GW_LOGP: sets the filename of the log file to use");
            Logger.Information("_TPC_SP: sets the path to store certificates of trusted stations");
            Logger.Information("");
            Logger.Information("Command line arguments overrule environment variable settings.");
            Logger.Information("");

            // output the options
            Logger.Information("Options:");
            StringBuilder stringBuilder = new StringBuilder();
            StringWriter stringWriter = new StringWriter(stringBuilder);
            options.WriteOptionDescriptions(stringWriter);
            string[] helpLines = stringBuilder.ToString().Split("\n");
            foreach (var line in helpLines)
            {
                Logger.Information(line);
            }
        }

        /// <summary>
        /// Default certificate validation callback
        /// </summary>
        private static void CertificateValidator_Default(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                Logger.Information($"O{ProgramName} does not trust the server with the certificate subject '{e.Certificate.Subject}'.");
                Logger.Information("If you want to trust this certificate, please copy it from the directory:");
                Logger.Information($"{OpcApplicationConfiguration.SecurityConfiguration.RejectedCertificateStore.StorePath}/certs");
                Logger.Information("to the directory:");
                Logger.Information($"{OpcApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StorePath}/certs");
            }
        }

        /// <summary>
        /// Auto trust server certificate validation callback
        /// </summary>
        private static void CertificateValidator_AutoTrustServerCerts(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                Logger.Information($"Certificate '{e.Certificate.Subject}' will be trusted, since the autotrustservercerts options was specified.");
                e.Accept = true;
                return;
            }
        }

        /// <summary>
        /// Handler for server status changes.
        /// </summary>
        private static void ServerEventStatus(Session session, SessionEventReason reason)
        {
            PrintSessionStatus(session, reason.ToString());
        }

        /// <summary>
        /// Shows the session status.
        /// </summary>
        private static void PrintSessionStatus(Session session, string reason)
        {
            lock (session.DiagnosticsLock)
            {
                string item = String.Format("{0,9}:{1,20}:", reason, session.SessionDiagnostics.SessionName);
                if (session.Identity != null)
                {
                    item += String.Format(":{0,20}", session.Identity.DisplayName);
                }
                item += String.Format(":{0}", session.Id);
                Logger.Information(item);
            }
        }

        /// <summary>
        /// Initialize logging.
        /// </summary>
        private static void InitLogging()
        {
            LoggerConfiguration loggerConfiguration = new LoggerConfiguration();

            // set the log level
            switch (_logLevel)
            {
                case "fatal":
                    loggerConfiguration.MinimumLevel.Fatal();
                    OpcTraceToLoggerFatal = 0;
                    break;
                case "error":
                    loggerConfiguration.MinimumLevel.Error();
                    OpcStackTraceMask = OpcTraceToLoggerError = Utils.TraceMasks.Error;
                    break;
                case "warn":
                    loggerConfiguration.MinimumLevel.Warning();
                    OpcTraceToLoggerWarning = 0;
                    break;
                case "info":
                    loggerConfiguration.MinimumLevel.Information();
                    OpcStackTraceMask = OpcTraceToLoggerInformation = 0;
                    break;
                case "debug":
                    loggerConfiguration.MinimumLevel.Debug();
                    OpcStackTraceMask = OpcTraceToLoggerDebug = Utils.TraceMasks.StackTrace | Utils.TraceMasks.Operation |
                        Utils.TraceMasks.StartStop | Utils.TraceMasks.ExternalSystem | Utils.TraceMasks.Security;
                    break;
                case "verbose":
                    loggerConfiguration.MinimumLevel.Verbose();
                    OpcStackTraceMask = OpcTraceToLoggerVerbose = Utils.TraceMasks.All;
                    break;
            }

            // set logging sinks
            loggerConfiguration.WriteTo.Console();

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("_GW_LOGP")))
            {
                _logFileName = Environment.GetEnvironmentVariable("_GW_LOGP");
            }

            if (!string.IsNullOrEmpty(_logFileName))
            {
                // configure rolling file sink
                const int MAX_LOGFILE_SIZE = 1024 * 1024;
                const int MAX_RETAINED_LOGFILES = 2;
                loggerConfiguration.WriteTo.File(_logFileName, fileSizeLimitBytes: MAX_LOGFILE_SIZE, flushToDiskInterval: _logFileFlushTimeSpanSec, rollOnFileSizeLimit: true, retainedFileCountLimit: MAX_RETAINED_LOGFILES);
            }

            Logger = loggerConfiguration.CreateLogger();
            Logger.Information($"Current directory is: {System.IO.Directory.GetCurrentDirectory()}");
            Logger.Information($"Log file is: {_logFileName}");
            Logger.Information($"Log level is: {_logLevel}");
            return;
        }

        private static string _logFileName = $"{Utils.GetHostName()}-{ProgramName.ToLower()}.log";
        private static string _logLevel = "info";
        private static TimeSpan _logFileFlushTimeSpanSec = TimeSpan.FromSeconds(30);
    }
}
