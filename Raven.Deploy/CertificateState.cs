using Raven.Client.ServerWide.Operations.Certificates;
using System.Collections.Generic;

namespace Raven.Deploy
{
    public class CertificateState
    {
        public string Name;
        public string Base64;
        public Dictionary<string, DatabaseAccess> Permissions;
        public SecurityClearance Clearance;
    }
}
