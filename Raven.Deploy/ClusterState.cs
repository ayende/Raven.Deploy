using Raven.Client.Documents.Operations.Configuration;
using Raven.Client.ServerWide.Operations.Configuration;
using Raven.Client.ServerWide.Operations.OngoingTasks;
using System.Collections.Generic;

namespace Raven.Deploy
{
    public class ClusterState
    {
        public Dictionary<string, DatabaseState> Databases;

        public ServerWideSettings ServerWide;
        
        public List<CertificateState> Certificates;

    }

    public class ServerWideSettings
    {
        public List<ServerWideBackupConfiguration> Backups;

        public List<ServerWideExternalReplication> ExternalReplications;

        public ClientConfiguration Client;

    }
}
