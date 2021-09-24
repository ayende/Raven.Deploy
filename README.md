# Raven.Deploy

Allow to store RavenDB deployments as configuration file. 

This is intentionally written to be as simple as possible, if a bit verbose.

The idea is that you can define your desired RavenDB state ( databases, certificates, ETL operations, etc) as a JSON/YAML file and point that to a cluster. The tool will then make sure that the system is setup properly for this need.

See here for additional motivation: https://issues.hibernatingrhinos.com/issue/RavenDB-16393