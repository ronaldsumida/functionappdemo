using System;
using System.IO;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;

namespace FunctionsLab
{
    public class BlobImageAnalysis
    {
        [FunctionName("BlobImageAnalysis")]
        public async Task Run([BlobTrigger("uploaded/{name}", Connection = "AzureWebJobsStorage")] Stream myBlob, string name, ILogger log)
        {
            var array = await ToByteArrayAsync(myBlob);
            var result = await AnalyzeImageAsync(array, log);

            if (result.adult.isAdultContent || result.adult.isRacyContent)
            {
                // Copy blob to the "rejected" container
                await StoreBlobWithMetadataAsync(myBlob, "rejected", name, result, log);
            }
            else
            {
                // Copy blob to the "accepted" container
                await StoreBlobWithMetadataAsync(myBlob, "accepted", name, result, log);
            }
        }

        private async static Task<ImageAnalysisInfo> AnalyzeImageAsync(byte[] bytes, ILogger log)
        {
            HttpClient client = new HttpClient();

            // Set SubscriptionKey value for Vision Endpoint as Application Setting for the Function App
            var key = Environment.GetEnvironmentVariable("SubscriptionKey");

            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", key);

            HttpContent payload = new ByteArrayContent(bytes);
            payload.Headers.ContentType = new MediaTypeWithQualityHeaderValue("application/octet-stream");

            // Set VisionEndpoint value for Vision Endpoint as Application Setting for the Function App
            var endpoint = Environment.GetEnvironmentVariable("VisionEndpoint");
            var results = await client.PostAsync(endpoint + "/analyze?visualFeatures=Adult", payload);
            var result = await results.Content.ReadAsAsync<ImageAnalysisInfo>();
            return result;
        }

        // Writes a blob to a specified container and stores metadata with it
        private static async Task StoreBlobWithMetadataAsync(Stream image, string containerName, string blobName, ImageAnalysisInfo info, ILogger log)
        {
            log.LogInformation($"Writing blob and metadata to \"{containerName}\" container...");

            //var connection = "DefaultEndpointsProtocol=https;AccountName=msfunctionlab;AccountKey=YsqHTIllAxnIvaoPbaUK3XGXVSEXjNM/oViScgH6FKWkkGWCF4165eYQ/CO5WqtuZPwQRcGfOFXP+AStWUjxSg==;EndpointSuffix=core.windows.net";
            var connection = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            var account = CloudStorageAccount.Parse(connection);
            var client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference(containerName);

            try
            {
                var blob = container.GetBlockBlobReference(blobName);

                if (blob != null)
                {
                    // Upload the blob
                    await blob.UploadFromStreamAsync(image);

                    // Get the blob attributes
                    await blob.FetchAttributesAsync();

                    // Write the blob metadata
                    blob.Metadata["isAdultContent"] = info.adult.isAdultContent.ToString();
                    blob.Metadata["adultScore"] = info.adult.adultScore.ToString("P0").Replace(" ", "");
                    blob.Metadata["isRacyContent"] = info.adult.isRacyContent.ToString();
                    blob.Metadata["racyScore"] = info.adult.racyScore.ToString("P0").Replace(" ", "");

                    // Save the blob metadata
                    await blob.SetMetadataAsync();
                }
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
            }
        }

        // Converts a stream to a byte array
        private async static Task<byte[]> ToByteArrayAsync(Stream stream)
        {
            Int32 length = stream.Length > Int32.MaxValue ? Int32.MaxValue : Convert.ToInt32(stream.Length);
            byte[] buffer = new Byte[length];
            await stream.ReadAsync(buffer, 0, length);
            stream.Position = 0;
            return buffer;
        }

        public class ImageAnalysisInfo
        {
            public Adult adult { get; set; }
            public string requestId { get; set; }
        }

        public class Adult
        {
            public bool isAdultContent { get; set; }
            public bool isRacyContent { get; set; }
            public float adultScore { get; set; }
            public float racyScore { get; set; }
        }

    }
}
