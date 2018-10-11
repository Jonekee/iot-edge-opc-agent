
using Opc.Ua;
using System;
using System.Security.Cryptography.X509Certificates;

namespace OpcAgent
{
    using System.Threading.Tasks;
    using static Opc.Ua.CertificateStoreType;
    using static Program;

    public class OpcStackConfiguration
    {
        public static ApplicationConfiguration OpcApplicationConfiguration { get; private set; }
        public static string ApplicationName { get; set; } = ProgramName.ToLower();

        public static int OpcMaxStringLength { get; set; } = 1024 * 1024;

        public static int OpcOperationTimeout { get; set; } = 120000;

        public static bool TrustMyself { get; set; } = true;

        public static int OpcStackTraceMask { get; set; } = Utils.TraceMasks.Error | Utils.TraceMasks.Security | Utils.TraceMasks.StackTrace | Utils.TraceMasks.StartStop;

        public static bool OpcAutoTrustServerCerts { get; set; } = false;

        public static uint OpcSessionCreationTimeout { get; set; } = 10;

        public static uint OpcSessionCreationBackoffMax { get; set; } = 5;

        public static uint OpcKeepAliveDisconnectThreshold { get; set; } = 5;

        public static int OpcKeepAliveIntervalInSec { get; set; } = 2;


        public const int OpcSamplingIntervalDefault = 1000;

        public static int OpcSamplingInterval { get; set; } = OpcSamplingIntervalDefault;


        public const int OpcPublishingIntervalDefault = 0;

        public static int OpcPublishingInterval { get; set; } = OpcPublishingIntervalDefault;

        public static string OpcOwnCertStoreType { get; set; } = X509Store;


        public static string OpcOwnCertDirectoryStorePathDefault => "CertificateStores/own";

        public static string OpcOwnCertX509StorePathDefault => "CurrentUser\\UA_MachineDefault";

        public static string OpcOwnCertStorePath { get; set; } = OpcOwnCertX509StorePathDefault;

        public static string OpcTrustedCertStoreType { get; set; } = Directory;

        public static string OpcTrustedCertDirectoryStorePathDefault => "CertificateStores/trusted";

        public static string OpcTrustedCertX509StorePathDefault => "CurrentUser\\UA_MachineDefault";

        public static string OpcTrustedCertStorePath { get; set; } = null;

        public static string OpcRejectedCertStoreType { get; set; } = Directory;

        public static string OpcRejectedCertDirectoryStorePathDefault => "CertificateStores/rejected";

        public static string OpcRejectedCertX509StorePathDefault => "CurrentUser\\UA_MachineDefault";

        public static string OpcRejectedCertStorePath { get; set; } = OpcRejectedCertDirectoryStorePathDefault;

        public static string OpcIssuerCertStoreType { get; set; } = Directory;


        public static string OpcIssuerCertDirectoryStorePathDefault => "CertificateStores/issuers";

        public static string OpcIssuerCertX509StorePathDefault => "CurrentUser\\UA_MachineDefault";

        public static string OpcIssuerCertStorePath { get; set; } = OpcIssuerCertDirectoryStorePathDefault;

        public static int OpcTraceToLoggerVerbose { get; set; } = 0;
        public static int OpcTraceToLoggerDebug { get; set; } = 0;
        public static int OpcTraceToLoggerInformation { get; set; } = 0;
        public static int OpcTraceToLoggerWarning { get; set; } = 0;
        public static int OpcTraceToLoggerError { get; set; } = 0;
        public static int OpcTraceToLoggerFatal { get; set; } = 0;

        /// <summary>
        /// Configures all OPC stack settings
        /// </summary>
        public async Task ConfigureAsync()
        {
            // Instead of using a Config.xml we configure everything programmatically.

            //
            // OPC UA Application configuration
            //
            OpcApplicationConfiguration = new ApplicationConfiguration();

            // Passed in as command line argument
            OpcApplicationConfiguration.ApplicationName = ApplicationName;
            OpcApplicationConfiguration.ApplicationUri = $"urn:{Utils.GetHostName()}:{OpcApplicationConfiguration.ApplicationName}:microsoft:";
            OpcApplicationConfiguration.ProductUri = "https://github.com/hansgschossmann/iot-edge-opc-agent";
            OpcApplicationConfiguration.ApplicationType = ApplicationType.Client;


            //
            // Security configuration
            //
            OpcApplicationConfiguration.SecurityConfiguration = new SecurityConfiguration();

            // Application certificate
            OpcApplicationConfiguration.SecurityConfiguration.ApplicationCertificate = new CertificateIdentifier();
            OpcApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.StoreType = OpcOwnCertStoreType;
            OpcApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.StorePath = OpcOwnCertStorePath;
            OpcApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.SubjectName = OpcApplicationConfiguration.ApplicationName;
            Logger.Information($"Application Certificate store type is: {OpcApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.StoreType}");
            Logger.Information($"Application Certificate store path is: {OpcApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.StorePath}");
            Logger.Information($"Application Certificate subject name is: {OpcApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.SubjectName}");

            // Use existing certificate, if it is there.
            X509Certificate2 certificate = await OpcApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Find(true);
            if (certificate == null)
            {
                Logger.Information($"No existing Application certificate found. Create a self-signed Application certificate valid from yesterday for {CertificateFactory.defaultLifeTime} months,");
                Logger.Information($"with a {CertificateFactory.defaultKeySize} bit key and {CertificateFactory.defaultHashSize} bit hash.");
                certificate = CertificateFactory.CreateCertificate(
                    OpcApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.StoreType,
                    OpcApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.StorePath,
                    null,
                    OpcApplicationConfiguration.ApplicationUri,
                    OpcApplicationConfiguration.ApplicationName,
                    OpcApplicationConfiguration.ApplicationName,
                    null,
                    CertificateFactory.defaultKeySize,
                    DateTime.UtcNow - TimeSpan.FromDays(1),
                    CertificateFactory.defaultLifeTime,
                    CertificateFactory.defaultHashSize,
                    false,
                    null,
                    null
                    );
                OpcApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate = certificate ?? throw new Exception("OPC UA application certificate can not be created! Cannot continue without it!");
            }
            else
            {
                Logger.Information("Application certificate found in Application Certificate Store");
            }
            OpcApplicationConfiguration.ApplicationUri = Utils.GetApplicationUriFromCertificate(certificate);
            Logger.Information($"Application certificate is for Application URI '{OpcApplicationConfiguration.ApplicationUri}', Application '{OpcApplicationConfiguration.ApplicationName} and has Subject '{OpcApplicationConfiguration.ApplicationName}'");

            // TrustedIssuerCertificates
            OpcApplicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates = new CertificateTrustList();
            OpcApplicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StoreType = OpcIssuerCertStoreType;
            OpcApplicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StorePath = OpcIssuerCertStorePath;
            Logger.Information($"Trusted Issuer store type is: {OpcApplicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StoreType}");
            Logger.Information($"Trusted Issuer Certificate store path is: {OpcApplicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StorePath}");

            // TrustedPeerCertificates
            OpcApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates = new CertificateTrustList();
            OpcApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StoreType = OpcTrustedCertStoreType;
            if (string.IsNullOrEmpty(OpcTrustedCertStorePath))
            {
                // Set default.
                OpcApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StorePath = OpcTrustedCertStoreType == X509Store ? OpcTrustedCertX509StorePathDefault : OpcTrustedCertDirectoryStorePathDefault;
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("_TPC_SP")))
                {
                    // Use environment variable.
                    OpcApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StorePath = Environment.GetEnvironmentVariable("_TPC_SP");
                }
            }
            else
            {
                OpcApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StorePath = OpcTrustedCertStorePath;
            }
            Logger.Information($"Trusted Peer Certificate store type is: {OpcApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StoreType}");
            Logger.Information($"Trusted Peer Certificate store path is: {OpcApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StorePath}");

            // RejectedCertificateStore
            OpcApplicationConfiguration.SecurityConfiguration.RejectedCertificateStore = new CertificateTrustList();
            OpcApplicationConfiguration.SecurityConfiguration.RejectedCertificateStore.StoreType = OpcRejectedCertStoreType;
            OpcApplicationConfiguration.SecurityConfiguration.RejectedCertificateStore.StorePath = OpcRejectedCertStorePath;
            Logger.Information($"Rejected certificate store type is: {OpcApplicationConfiguration.SecurityConfiguration.RejectedCertificateStore.StoreType}");
            Logger.Information($"Rejected Certificate store path is: {OpcApplicationConfiguration.SecurityConfiguration.RejectedCertificateStore.StorePath}");

            // AutoAcceptUntrustedCertificates
            // This is a security risk and should be set to true only for debugging purposes.
            OpcApplicationConfiguration.SecurityConfiguration.AutoAcceptUntrustedCertificates = false;

            // RejectSHA1SignedCertificates
            // We allow SHA1 certificates for now as many OPC Servers still use them
            OpcApplicationConfiguration.SecurityConfiguration.RejectSHA1SignedCertificates = false;
            Logger.Information($"Rejection of SHA1 signed certificates is {(OpcApplicationConfiguration.SecurityConfiguration.RejectSHA1SignedCertificates ? "enabled" : "disabled")}");

            // MinimunCertificatesKeySize
            // We allow a minimum key size of 1024 bit, as many OPC UA servers still use them
            OpcApplicationConfiguration.SecurityConfiguration.MinimumCertificateKeySize = 1024;
            Logger.Information($"Minimum certificate key size set to {OpcApplicationConfiguration.SecurityConfiguration.MinimumCertificateKeySize}");

            // We make the default reference stack behavior configurable to put our own certificate into the trusted peer store.
            if (TrustMyself)
            {
                // Ensure it is trusted
                try
                {
                    ICertificateStore store = OpcApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.OpenStore();
                    if (store == null)
                    {
                        Logger.Information($"Can not open trusted peer store. StorePath={OpcApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StorePath}");
                    }
                    else
                    {
                        try
                        {
                            Logger.Information($"Adding own certificate to trusted peer store. StorePath={OpcApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StorePath}");
                            X509Certificate2 publicKey = new X509Certificate2(certificate.RawData);
                            X509Certificate2Collection certCollection = await store.FindByThumbprint(publicKey.Thumbprint);
                            if (certCollection.Count > 0)
                            {
                                Logger.Information($"A certificate with the same thumbprint is already in the trusted store.");
                            }
                            else
                            {
                                await store.Add(publicKey);
                            }
                        }
                        finally
                        {
                            store.Close();
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Can not add own certificate to trusted peer store. StorePath={OpcApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StorePath}");
                }
            }
            else
            {
                Logger.Information($"{ProgramName} certificate is not added to trusted peer store.");
            }


            //
            // TransportConfigurations
            //

            OpcApplicationConfiguration.TransportQuotas = new TransportQuotas();
            OpcApplicationConfiguration.TransportQuotas.MaxByteStringLength = 4 * 1024 * 1024;
            OpcApplicationConfiguration.TransportQuotas.MaxMessageSize = 4 * 1024 * 1024;

            // the maximum string length could be set to ajust for large number of nodes when reading the list of published nodes
            OpcApplicationConfiguration.TransportQuotas.MaxStringLength = OpcMaxStringLength;
            
            // the OperationTimeout should be twice the minimum value for PublishingInterval * KeepAliveCount, so set to 120s
            OpcApplicationConfiguration.TransportQuotas.OperationTimeout = OpcOperationTimeout;
            Logger.Information($"OperationTimeout set to {OpcApplicationConfiguration.TransportQuotas.OperationTimeout}");

            //
            // TraceConfiguration
            //
            //
            // TraceConfiguration
            //
            OpcApplicationConfiguration.TraceConfiguration = new TraceConfiguration();
            OpcApplicationConfiguration.TraceConfiguration.TraceMasks = OpcStackTraceMask;
            OpcApplicationConfiguration.TraceConfiguration.ApplySettings();
            Utils.Tracing.TraceEventHandler += new EventHandler<TraceEventArgs>(LoggerOpcUaTraceHandler);
            Logger.Information($"opcstacktracemask set to: 0x{OpcStackTraceMask:X}");

            // add default client configuration
            OpcApplicationConfiguration.ClientConfiguration = new ClientConfiguration();

            // validate the configuration now
            await OpcApplicationConfiguration.Validate(OpcApplicationConfiguration.ApplicationType);
        }

        /// <summary>
        /// Event handler to log OPC UA stack trace messages into own logger.
        /// </summary>
        private static void LoggerOpcUaTraceHandler(object sender, TraceEventArgs e)
        {
            // return fast if no trace needed
            if ((e.TraceMask & OpcStackTraceMask) == 0)
            {
                return;
            }

            // e.Exception and e.Message are always null

            // format the trace message
            string message = string.Empty;
            message = string.Format(e.Format, e.Arguments)?.Trim();
            message = "OPC: " + message;

            // map logging level
            if ((e.TraceMask & OpcTraceToLoggerVerbose) != 0)
            {
                Logger.Verbose(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerDebug) != 0)
            {
                Logger.Debug(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerInformation) != 0)
            {
                Logger.Information(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerWarning) != 0)
            {
                Logger.Warning(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerError) != 0)
            {
                Logger.Error(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerFatal) != 0)
            {
                Logger.Fatal(message);
                return;
            }
            return;
        }
    }
}
