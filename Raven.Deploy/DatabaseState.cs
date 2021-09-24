using Raven.Client.Documents.Indexes.Analysis;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Queries.Sorting;
using Raven.Client.ServerWide;
using System.Collections.Generic;
using Raven.Client.Documents.Operations.Expiration;
using Raven.Client.Documents.Operations.Refresh;
using Raven.Client.Documents.Operations.Revisions;
using Raven.Client.Documents.Operations.TimeSeries;
using Raven.Client.Documents.Operations.Backups;
using Raven.Client.Documents.Operations.Configuration;
using Raven.Client.Documents.Operations.ETL.OLAP;
using Raven.Client.Documents.Operations.ETL.SQL;
using Raven.Client.Documents.Operations.ETL;
using Raven.Client.Documents.Operations.Replication;

namespace Raven.Deploy
{
    public class DatabaseState
    {
        public DocumentsCompressionConfiguration DocumentsCompression;
        public bool Disabled;
        public bool Encrypted;
        public DatabaseLockMode LockMode;

        public int MinimumReplicationFactor;
        public ConflictSolver ConflictSolverConfig;

        public Dictionary<string, SorterDefinition> Sorters = new Dictionary<string, SorterDefinition>();

        public Dictionary<string, AnalyzerDefinition> Analyzers = new Dictionary<string, AnalyzerDefinition>();

        public Dictionary<string, IndexDefinition> Indexes;

        public Dictionary<string, string> Settings = new Dictionary<string, string>();

        public RevisionsConfiguration Revisions;

        public TimeSeriesConfiguration TimeSeries;

        public RevisionsCollectionConfiguration RevisionsForConflicts;

        public ExpirationConfiguration Expiration;

        public RefreshConfiguration Refresh;

        public List<PeriodicBackupConfiguration> PeriodicBackups = new List<PeriodicBackupConfiguration>();

        public List<ExternalReplication> ExternalReplications = new List<ExternalReplication>();

        public List<PullReplicationAsSink> SinkPullReplications = new List<PullReplicationAsSink>();

        public List<PullReplicationDefinition> HubPullReplications = new List<PullReplicationDefinition>();

        public Dictionary<string, RavenConnectionString> RavenConnectionStrings = new Dictionary<string, RavenConnectionString>();

        public Dictionary<string, SqlConnectionString> SqlConnectionStrings = new Dictionary<string, SqlConnectionString>();

        public Dictionary<string, OlapConnectionString> OlapConnectionStrings = new Dictionary<string, OlapConnectionString>();

        public List<RavenEtlConfiguration> RavenEtls = new List<RavenEtlConfiguration>();

        public List<SqlEtlConfiguration> SqlEtls = new List<SqlEtlConfiguration>();

        public List<OlapEtlConfiguration> OlapEtls = new List<OlapEtlConfiguration>();

        public ClientConfiguration Client;
    }
}