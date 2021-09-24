using Newtonsoft.Json;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Indexes.Analysis;
using Raven.Client.Documents.Operations.Analyzers;
using Raven.Client.Documents.Operations.Backups;
using Raven.Client.Documents.Operations.Configuration;
using Raven.Client.Documents.Operations.ConnectionStrings;
using Raven.Client.Documents.Operations.ETL;
using Raven.Client.Documents.Operations.ETL.OLAP;
using Raven.Client.Documents.Operations.ETL.SQL;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Client.Documents.Operations.Replication;
using Raven.Client.Documents.Operations.Sorters;
using Raven.Client.Documents.Queries.Sorting;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Raven.Client.ServerWide.Operations.Certificates;
using Raven.Client.ServerWide.Operations.Configuration;
using Raven.Client.ServerWide.Operations.OngoingTasks;
using Sparrow.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using static Raven.Client.Constants;

namespace Raven.Deploy
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: Raven.Deploy <server-url> <config.yaml or config.json> <certficate.pfx>");
                Environment.Exit(1);
                return;
            }

            var url = args[0];
            var config = args[1];
            X509Certificate2 cert = null;
            if (args.Length > 2)
            {
                cert = new X509Certificate2(args[2]);
            }

            var text = File.ReadAllText(config);
            ClusterState desiredState = null;

            try
            {
                desiredState = Path.GetExtension(config).ToLowerInvariant() switch
                {
                    ".json" => FromJson(text),
                    ".yaml" => FromJson(JsonConvert.SerializeObject(new Deserializer().Deserialize(new StringReader(text)))),
                    _ => throw new ArgumentOutOfRangeException("File " + config + " must be .json or .yaml")
                };
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to parse file: " + config);
                Console.WriteLine(e);
                Environment.Exit(77);
                return;
            }

            var documentStore = new DocumentStore
            {
                Urls = new[] { url },
                Certificate = cert
            };
            documentStore.Initialize();

            foreach (var certificate in desiredState.Certificates ?? Enumerable.Empty<CertificateState>())
            {
                try
                {
                    var x509 = new X509Certificate2(Convert.FromBase64String(certificate.Base64));
                    var putCert = new PutClientCertificateOperation(certificate.Name, x509, certificate.Permissions, certificate.Clearance);

                    var result = await documentStore.Maintenance.Server.SendAsync(new GetCertificateOperation(x509.Thumbprint));
                    if (result != null && result.SecurityClearance == certificate.Clearance && NoChange(result.Permissions, certificate.Permissions))
                    {
                        Console.WriteLine($"No change for certificate {certificate.Name} ({x509.Thumbprint}) configuration.");
                        continue;
                    }

                    Console.Write("Creating certificate " + certificate.Name + "... ");
                    await documentStore.Maintenance.Server.SendAsync(putCert);
                    Console.WriteLine("Success");
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Failure!");
                    Console.Error.WriteLine(e);
                    Environment.Exit(2);
                    return;
                }
            }

            if (desiredState.ServerWide?.Client != null)
            {
                Console.Write("Setting server wide client configuration... ");
                try
                {
                    await documentStore.Maintenance.Server.SendAsync(new PutServerWideClientConfigurationOperation(desiredState.ServerWide.Client));
                    Console.WriteLine("Success.");
                }
                catch (Exception e)
                {
                    Console.Error.Write("Failure!");
                    Console.Error.WriteLine(e);
                    Environment.Exit(3);
                    return;
                }
            }
            foreach (var backup in desiredState.ServerWide?.Backups ?? Enumerable.Empty<ServerWideBackupConfiguration>())
            {
                try
                {
                    var result = await documentStore.Maintenance.Server.SendAsync(new GetServerWideBackupConfigurationOperation(backup.Name));
                    if (result != null && NoChange(result, backup))
                    {
                        Console.WriteLine($"No change for server wide backup configuration {backup.Name}.");
                        continue;
                    }
                    Console.WriteLine($"Creating server wide backup configuration: {backup.Name}... ");
                    await documentStore.Maintenance.Server.SendAsync(new PutServerWideBackupConfigurationOperation(backup));
                    Console.WriteLine("Success.");
                }
                catch (Exception e)
                {
                    Console.Error.Write("Failure!");
                    Console.Error.WriteLine(e);
                    Environment.Exit(4);
                    return;
                }
            }
            foreach (var repl in desiredState.ServerWide?.ExternalReplications ?? Enumerable.Empty<ServerWideExternalReplication>())
            {
                try
                {
                    var result = await documentStore.Maintenance.Server.SendAsync(new GetServerWideExternalReplicationOperation(repl.Name));
                    if (result != null && NoChange(result, repl))
                    {
                        Console.WriteLine($"No change for server wide external replication {repl.Name}.");
                        continue;
                    }
                    Console.WriteLine($"Creating server wide external replication: {repl.Name}... ");
                    await documentStore.Maintenance.Server.SendAsync(new PutServerWideExternalReplicationOperation(repl));
                    Console.WriteLine("Success.");
                }
                catch (Exception e)
                {
                    Console.Error.Write("Failure!");
                    Console.Error.WriteLine(e);
                    Environment.Exit(5);
                    return;
                }

            }
            foreach (var (name, db) in desiredState.Databases ?? new Dictionary<string, DatabaseState>())
            {
                DatabaseRecordWithEtag existing;
                try
                {
                    existing = await documentStore.Maintenance.Server.SendAsync(new GetDatabaseRecordOperation(name));
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Failed to get database record for: " + name);
                    Console.WriteLine(e);
                    Environment.Exit(6);
                    return;
                }
                if (existing == null)
                {
                    existing = new DatabaseRecordWithEtag()
                    {
                        DatabaseName = name,
                        Etag = -1,
                        Encrypted = db.Encrypted,
                        Disabled = db.Disabled,
                        Settings = db.Settings,
                        LockMode = db.LockMode,
                        DocumentsCompression = db.DocumentsCompression,
                        ConflictSolverConfig = db.ConflictSolverConfig,
                        Revisions = db.Revisions,
                        TimeSeries = db.TimeSeries,
                        RevisionsForConflicts = db.RevisionsForConflicts,
                        Expiration = db.Expiration,
                        Refresh = db.Refresh,
                        Client = db.Client,
                    };
                    Console.WriteLine($"Creating database: {name}... ");

                    try
                    {
                        var result = await documentStore.Maintenance.Server.SendAsync(new CreateDatabaseOperation(existing, Math.Max(1, db.MinimumReplicationFactor)));
                        existing.Topology = result.Topology;
                        existing.Etag = result.RaftCommandIndex;
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(7);
                        return;
                    }
                }
                if (existing.Encrypted != db.Encrypted ||
                        existing.Disabled != db.Disabled ||
                        existing.LockMode != db.LockMode ||
                        NoChange(existing.DocumentsCompression, db.DocumentsCompression) == false ||
                        NoChange(existing.ConflictSolverConfig, db.ConflictSolverConfig) == false ||
                        NoChange(existing.Revisions, db.Revisions) == false ||
                        NoChange(existing.TimeSeries, db.TimeSeries) == false ||
                        NoChange(existing.RevisionsForConflicts, db.RevisionsForConflicts) == false ||
                        NoChange(existing.Expiration, db.Expiration) == false ||
                        NoChange(existing.Refresh, db.Refresh) == false ||
                        NoChange(existing.Client, db.Client) == false)
                {
                    existing.Encrypted = db.Encrypted;
                    existing.Disabled = db.Disabled;
                    existing.Settings = db.Settings;
                    existing.LockMode = db.LockMode;
                    existing.DocumentsCompression = db.DocumentsCompression;
                    existing.ConflictSolverConfig = db.ConflictSolverConfig;
                    existing.Revisions = db.Revisions;
                    existing.TimeSeries = db.TimeSeries;
                    existing.RevisionsForConflicts = db.RevisionsForConflicts;
                    existing.Expiration = db.Expiration;
                    existing.Refresh = db.Refresh;
                    existing.Client = db.Client;
                    var result = await documentStore.Maintenance.Server.SendAsync(new UpdateDatabaseOperation(existing, existing.Etag));
                    existing.Topology = result.Topology;
                    existing.Etag = result.RaftCommandIndex;

                }
                var additionalNodes = db.MinimumReplicationFactor - existing.Topology.Count;
                for (int i = 0; i < additionalNodes; i++)
                {
                    Console.WriteLine($"Adding node to database: {name}... ");
                    try
                    {
                        var result = await documentStore.Maintenance.Server.SendAsync(new AddDatabaseNodeOperation(name));
                        existing.Topology = result.Topology;
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(74);
                        return;
                    }
                }
                var dbOps = documentStore.Maintenance.ForDatabase(name);
                foreach (var (n, v) in db.Analyzers ?? new Dictionary<string, AnalyzerDefinition>())
                {
                    if (existing.Analyzers != null &&
                        existing.Analyzers.TryGetValue(n, out var definition) &&
                        NoChange(v, definition))
                    {
                        continue;
                    }
                    try
                    {
                        Console.WriteLine($"Creating analyzer {n} for database {name}... ");
                        v.Name = n;
                        await dbOps.SendAsync(new PutAnalyzersOperation(v));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(8);
                        return;
                    }
                }
                foreach (var (n, v) in db.Indexes ?? new Dictionary<string, IndexDefinition>())
                {
                    if (existing.Indexes != null &&
                        existing.Indexes.TryGetValue(n, out var definition) &&
                        NoChange(v, definition))
                    {
                        continue;
                    }
                    try
                    {
                        Console.WriteLine($"Creating index {n} for database {name}... ");
                        v.Name = n;
                        await dbOps.SendAsync(new PutIndexesOperation(v));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(9);
                        return;
                    }
                }
                foreach (var (n, v) in db.Sorters ?? new Dictionary<string, SorterDefinition>())
                {
                    if (existing.Sorters != null &&
                        existing.Sorters.TryGetValue(n, out var definition) &&
                        NoChange(v, definition))
                    {
                        continue;
                    }
                    try
                    {
                        Console.WriteLine($"Creating sorter {n} for database {name}... ");
                        v.Name = n;
                        await dbOps.SendAsync(new PutSortersOperation(v));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(10);
                        return;
                    }
                }
                if (db.Settings != null && NoChange(existing.Settings, db.Settings) == false)
                {
                    existing.Settings ??= new Dictionary<string, string>();
                    foreach (var (k, v) in db.Settings)
                    {
                        existing.Settings[k] = v;
                    }
                    Console.WriteLine($"Updating database {name} settings... ");
                    try
                    {
                        await dbOps.SendAsync(new PutDatabaseSettingsOperation(name, existing.Settings));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(11);
                        return;
                    }
                }
                foreach (var backup in db.PeriodicBackups ?? Enumerable.Empty<PeriodicBackupConfiguration>())
                {
                    if (existing.PeriodicBackups != null &&
                        NoChange(backup, existing.PeriodicBackups.Find(b => b.Name == backup.Name)))
                    {
                        continue;
                    }
                    try
                    {
                        Console.WriteLine($"Creating periodic backup {backup.Name} for database {name}... ");
                        await dbOps.SendAsync(new BackupOperation(backup));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(12);
                        return;
                    }
                }
                foreach (var repl in db.ExternalReplications ?? Enumerable.Empty<ExternalReplication>())
                {
                    if (existing.ExternalReplications != null &&
                        NoChange(repl, existing.ExternalReplications.Find(b => b.Name == repl.Name)))
                    {
                        continue;
                    }
                    try
                    {
                        Console.WriteLine($"Creating external replication {repl.Name} for database {name}... ");
                        await dbOps.SendAsync(new UpdateExternalReplicationOperation(repl));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(13);
                        return;
                    }
                }
                foreach (var repl in db.SinkPullReplications ?? Enumerable.Empty<PullReplicationAsSink>())
                {
                    if (existing.SinkPullReplications != null &&
                        NoChange(repl, existing.SinkPullReplications.Find(b => b.Name == repl.Name)))
                    {
                        continue;
                    }
                    try
                    {
                        Console.WriteLine($"Creating sink pull replication {repl.Name} for database {name}... ");
                        await dbOps.SendAsync(new UpdatePullReplicationAsSinkOperation(repl));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(14);
                        return;
                    }
                }
                foreach (var repl in db.HubPullReplications ?? Enumerable.Empty<PullReplicationDefinition>())
                {
                    if (existing.HubPullReplications != null &&
                        NoChange(repl, existing.HubPullReplications.Find(b => b.Name == repl.Name)))
                    {
                        continue;
                    }
                    try
                    {
                        Console.WriteLine($"Creating hub pull replication {repl.Name} for database {name}... ");
                        await dbOps.SendAsync(new PutPullReplicationAsHubOperation(repl));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(15);
                        return;
                    }
                }
                foreach (var (n, v) in db.RavenConnectionStrings ?? new Dictionary<string, RavenConnectionString>())
                {
                    if (existing.RavenConnectionStrings != null &&
                        existing.RavenConnectionStrings.TryGetValue(n, out var definition) &&
                        NoChange(v, definition))
                    {
                        continue;
                    }
                    try
                    {
                        Console.WriteLine($"Creating RavenDB connection string {n} for database {name}... ");
                        await dbOps.SendAsync(new PutConnectionStringOperation<RavenConnectionString>(v));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(16);
                        return;
                    }
                }
                foreach (var (n, v) in db.SqlConnectionStrings ?? new Dictionary<string, SqlConnectionString>())
                {
                    if (existing.SqlConnectionStrings != null &&
                        existing.SqlConnectionStrings.TryGetValue(n, out var definition) &&
                        NoChange(v, definition))
                    {
                        continue;
                    }
                    try
                    {
                        Console.WriteLine($"Creating SQL connection string {n} for database {name}... ");
                        await dbOps.SendAsync(new PutConnectionStringOperation<SqlConnectionString>(v));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(17);
                        return;
                    }
                }
                foreach (var (n, v) in db.OlapConnectionStrings ?? new Dictionary<string, OlapConnectionString>())
                {
                    if (existing.OlapConnectionStrings != null &&
                        existing.OlapConnectionStrings.TryGetValue(n, out var definition) &&
                        NoChange(v, definition))
                    {
                        continue;
                    }
                    try
                    {
                        Console.WriteLine($"Creating OLAP connection string {n} for database {name}... ");
                        await dbOps.SendAsync(new PutConnectionStringOperation<OlapConnectionString>(v));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(18);
                        return;
                    }
                }
                foreach (var etl in db.RavenEtls ?? Enumerable.Empty<RavenEtlConfiguration>())
                {
                    if (existing.RavenEtls != null &&
                        NoChange(etl, existing.RavenEtls.Find(b => b.Name == etl.Name)))
                    {
                        continue;
                    }
                    try
                    {
                        Console.WriteLine($"Creating RavenDB ETL {etl.Name} for database {name}... ");
                        await dbOps.SendAsync(new AddEtlOperation<RavenConnectionString>(etl));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(19);
                        return;
                    }
                }
                foreach (var etl in db.SqlEtls ?? Enumerable.Empty<SqlEtlConfiguration>())
                {
                    if (existing.SqlEtls != null &&
                        NoChange(etl, existing.SqlEtls.Find(b => b.Name == etl.Name)))
                    {
                        continue;
                    }
                    try
                    {
                        Console.WriteLine($"Creating SQL ETL {etl.Name} for database {name}... ");
                        await dbOps.SendAsync(new AddEtlOperation<SqlConnectionString>(etl));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(19);
                        return;
                    }
                }
                foreach (var etl in db.OlapEtls ?? Enumerable.Empty<OlapEtlConfiguration>())
                {
                    if (existing.OlapEtls != null &&
                        NoChange(etl, existing.OlapEtls.Find(b => b.Name == etl.Name)))
                    {
                        continue;
                    }
                    try
                    {
                        Console.WriteLine($"Creating OLAP ETL {etl.Name} for database {name}... ");
                        await dbOps.SendAsync(new AddEtlOperation<OlapConnectionString>(etl));
                        Console.WriteLine("Success.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.Write("Failure!");
                        Console.Error.WriteLine(e);
                        Environment.Exit(20);
                        return;
                    }
                }
                if (NoChange(existing.Client, db.Client) == false)
                {
                    await dbOps.SendAsync(new PutClientConfigurationOperation(db.Client));
                }
            }
        }

        private static bool NoChange<T>(T c, T d)
            where T : class
        {
            if (c == null && d == null)
                return true;
            if (c == null || d == null)
                return false;
            // hacky, but works
            return JsonConvert.SerializeObject(c) == JsonConvert.SerializeObject(d);
        }

        private static ClusterState FromJson(string text)
        {
            return new JsonSerializer().Deserialize<ClusterState>(new JsonTextReader(new StringReader(text)));
        }
    }
}
