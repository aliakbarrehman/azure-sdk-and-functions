using System;
using System.IO;
using System.Text;
using Azure;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Microsoft.Azure.Management;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;

namespace Function
{
    public static class AzureHelper
    {
        private static IAzure azure;
        private static bool azureAuthenticated;
        public static string resourceGroupName = "byforby"; // change this when migrating code
        public static IAzure AuthenticateAzure()
        {
            var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
            var tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
            var subscription = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");
            var credentials = SdkContext.AzureCredentialsFactory.FromServicePrincipal(clientId, clientSecret, tenantId, AzureEnvironment.AzureGlobalCloud);
            azure = Microsoft.Azure.Management.Fluent.Azure.Configure()
                            .Authenticate(credentials)
                            .WithSubscription(subscription);
            azureAuthenticated = true;
            return azure;
        }

        public static bool AddTextDnsRecord(string domain, string dnsAddr, string dnsTxt)
        {
            if (!azureAuthenticated)
                return false;
            try
            {
                var dnsZone = azure.DnsZones.GetByResourceGroup(resourceGroupName, domain);
                dnsZone.Update()
                    .DefineTxtRecordSet(dnsAddr)
                        .WithText(dnsTxt)
                        .WithTimeToLive(1)
                        .Attach()
                    .Apply();
                return true;
            }
            catch (Exception _)
            {
                // Log Ex.Message here
                return false;
            }
        }

        public static bool RemoveTextDnsRecord(string domain, string dnsAddr)
        {
            if (!azureAuthenticated)
                return false;
            try
            {
                var dnsZone = azure.DnsZones.GetByResourceGroup(resourceGroupName, domain);
                dnsZone.Update()
                    .WithoutTxtRecordSet(dnsAddr)
                    .Apply();
                return true;
            }
            catch (Exception _)
            {
                // Log Ex.Message here
                return false;
            }
        }

        public static bool WriteStringToAzureFileShare(string storageConnString, string shareName, string fileName, string content)
        {
            if (!azureAuthenticated)
                return false;
            try
            {
                ShareClient share = new ShareClient(storageConnString, shareName);
                ShareDirectoryClient directory = share.GetRootDirectoryClient();
                directory.DeleteFile(fileName);
                ShareFileClient file = directory.GetFileClient(fileName);
                
                byte[] byteArray = Encoding.UTF8.GetBytes(content);
                using (MemoryStream stream = new MemoryStream(byteArray))
                {
                    file.Create(stream.Length);
                    file.UploadRange(
                        new HttpRange(0, stream.Length),
                        stream);
                }
                return true;
            }
            catch (Exception _)
            {
                // Log Ex.Message here
                return false;
            }
            
        }

        public static IContainerGroup DefineContainerGroup(IAzure azure, string containerGroupName, string azureRegion, string resourceGroupName, string registryUrl, string registryUserName, string registryPassword, string image, string dnsLabel)
        {
            var storageAccName = Environment.GetEnvironmentVariable("STORAGE_ACC_NAME");
            var storageAccKey = Environment.GetEnvironmentVariable("STORAGE_ACC_KEY");
            return azure.ContainerGroups.Define(containerGroupName)
                        .WithRegion(azureRegion)
                        .WithExistingResourceGroup(resourceGroupName)
                        .WithLinux()
                        .WithPrivateImageRegistry(registryUrl, registryUserName, registryPassword)
                        .DefineVolume("nginx-config")
                            .WithExistingReadWriteAzureFileShare("nginx-volume")
                            .WithStorageAccountName(storageAccName)
                            .WithStorageAccountKey(storageAccKey)
                            .Attach()
                        .DefineContainerInstance("nginx")
                            .WithImage("nginx")
                            .WithExternalTcpPort(443)
                            .WithCpuCoreCount(1.0)
                            .WithMemorySizeInGB(1.5)
                            .WithReadOnlyVolumeMountSetting("nginx-config", "/etc/nginx")
                            .Attach()
                        .DefineContainerInstance(containerGroupName)
                            .WithImage(image)
                            .WithExternalTcpPort(80)
                            .WithCpuCoreCount(1.0)
                            .WithMemorySizeInGB(1)
                            .Attach()
                        .WithDnsPrefix(dnsLabel)
                        .Create();
        }
    }
}