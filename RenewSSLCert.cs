using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Certes;
using Certes.Acme;
using System.Threading.Tasks;

namespace Function
{
    public static class RenewSSLCert
    {
        [FunctionName("RenewSSLCert")]
        public static async void Run([TimerTrigger("0 0 0 1 JAN,APR,JUL,OCT *")]TimerInfo myTimer, ILogger log)
        {
            var domainName = "cityforcity.com";
            var connString = Environment.GetEnvironmentVariable("STORAGE_CONN_STRING");
            var shareName = "nginx-volume";
            var ownerEmail = Environment.GetEnvironmentVariable("OWNER_EMAIL");
            var wildcardDomainName = $"*.{domainName}";
            var dnsAddr = $"_acme-challenge";

            AcmeContext acme = new AcmeContext(WellKnownServers.LetsEncryptV2); // change this for production, using staging to not get blocked
            await acme.NewAccount(ownerEmail, true);
            var order = await acme.NewOrder(new [] { wildcardDomainName });
            var orderUri = order.Location;
            var authorization = await order.Authorization(wildcardDomainName);
            var dnsChallenge = await authorization.Dns();
            var dnsTxt = acme.AccountKey.DnsTxt(dnsChallenge.Token);
            try {
                AzureHelper.AuthenticateAzure();
                var dnsUpdated = AzureHelper.AddTextDnsRecord(domainName, dnsAddr, dnsTxt);
                if (!dnsUpdated)
                    throw new Exception("Failed to update dns record");
                // wait for dns change to propogate
                await Task.Delay(500);

                var validatedChallege = await dnsChallenge.Validate();
                var startedValidation = DateTime.Now;
                while (validatedChallege.Status != Certes.Acme.Resource.ChallengeStatus.Valid)
                {
                    await Task.Delay(500);
                    if (DateTime.Now > startedValidation.AddSeconds(300))
                        throw new Exception("Failed to validate dns record");
                    validatedChallege = await dnsChallenge.Resource();
                }
                var dnsRemoved = AzureHelper.RemoveTextDnsRecord(domainName, dnsAddr);
                if (!dnsRemoved)
                    log.LogInformation("Failed to remove DNS record do it manually");
                
                var privateKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
                var certinfo = new CsrInfo();
                certinfo.CommonName = wildcardDomainName;
                var cert = await order.Generate(certinfo, privateKey);
                
                var certUpdated = AzureHelper.WriteStringToAzureFileShare(connString, shareName, "fullchain1.pem", cert.ToPem());
                if (!certUpdated)
                    throw new Exception("Failed to update certificate");
                var privKeyUpdated = AzureHelper.WriteStringToAzureFileShare(connString, shareName, "privKey1.pem", privateKey.ToPem());
                if (!privKeyUpdated)
                    throw new Exception("Failed to update private key");
            } catch (Exception _) {
                log.LogError(_.Message);
            }
        }
    }
}
