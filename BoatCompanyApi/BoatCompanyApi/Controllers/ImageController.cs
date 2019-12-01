using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BoatCompanyApi.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Diagnostics;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace BoatCompanyApi.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]")]
    public class ImageController : Controller
    {
        private const string visionSubscriptionKey = "b46d73d7012448a3b0476b5a139213b0";
        private IConfiguration _configuration;
        ComputerVisionClient computerVision;

        // Specify the features to return  
        private static readonly List<VisualFeatureTypes> features = new List<VisualFeatureTypes>()
        {
            VisualFeatureTypes.Objects,
            VisualFeatureTypes.Categories,
            VisualFeatureTypes.Description
        };

        public ImageController(ImageContext context, IConfiguration conf)
        {
            _configuration = conf;

            computerVision = new ComputerVisionClient(
                new ApiKeyServiceClientCredentials(visionSubscriptionKey),
                new System.Net.Http.DelegatingHandler[] { });

            computerVision.Endpoint = "https://boatcompanycomputervision.cognitiveservices.azure.com/";
        }

        // POST: api/image/upload
        [HttpPost("upload")]
        [DisableRequestSizeLimit]
        public async Task<ActionResult<ImageModel>> Post(IFormFile file)
        {
            var uploadSuccess = false;
            string uploadedUri = null;

            ImageModel imageResult = new ImageModel();

            // make sure file has been received
            if(file == null)
            {
                return imageResult;
            }
            else 
            {
                using (var stream = file.OpenReadStream())
                {
                    // Upload the file to blob
                    (uploadSuccess, uploadedUri) = await UploadToBlob(file.FileName, stream);
                    // TODO: Verify upload success

                    // Do the analysis, and save some results
                    var analysis = await AnalyzeRemoteAsync(computerVision, uploadedUri);
                    imageResult.Uri = uploadedUri;

                    if (analysis != null)
                    {
                        imageResult.descriptions = new List<string>();
                        imageResult.numObjects = analysis.Objects.Count;
                        foreach(var cap in analysis.Description.Captions)
                        {
                            imageResult.descriptions.Add(cap.Text);
                        }
                    }

                }
            }

            return imageResult;
        }

        // Do image analysis and show some debug information
        private static async Task<ImageAnalysis> AnalyzeRemoteAsync(ComputerVisionClient computerVision, string imageUrl)
        {
            if (!Uri.IsWellFormedUriString(imageUrl, UriKind.Absolute))
            {
                Debug.WriteLine("Invalid remoteImageUrl:\n{0} \n", imageUrl);
                return null;
            }

            ImageAnalysis analysis = await computerVision.AnalyzeImageAsync(imageUrl, features);
            DisplayResults(analysis, imageUrl);

            return analysis;
        }

        // Display results in console, for debuggin purposes
        private static void DisplayResults(ImageAnalysis analysis, string imageUri)
        {
            Debug.WriteLine("Analysis result for uri: " + imageUri);
            
            if (analysis != null)
            {
                Debug.WriteLine("Objects found: " + analysis.Objects.Count);
                foreach(ImageCaption cap in analysis.Description.Captions)
                {
                    Debug.WriteLine(cap.Text);
                }
            }
            else
            {
                Debug.WriteLine("No description generated.");
            }
            
        }

        // Make sure blob exists, and upload an image
        // Inspiration from: https://github.com/shahedc/SimpleUpload/blob/master/SimpleUpload/Controllers/HomeController.cs
        private async Task<(bool, string)> UploadToBlob(string filename, Stream stream = null)
        {
            CloudStorageAccount storageAccount = null;
            CloudBlobContainer cloudBlobContainer = null;
            string storageConnectionString = _configuration["BoatCompanyImageStorage"];

            // Check whether the connection string can be parsed.
            if (CloudStorageAccount.TryParse(storageConnectionString, out storageAccount))
            {
                try
                {
                    // Create the CloudBlobClient that represents the Blob storage endpoint for the storage account.
                    CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
                    // Get a reference to a blob
                    cloudBlobContainer = cloudBlobClient.GetContainerReference("uploadblob");
                    // ... and create it, if it does not exist
                    cloudBlobContainer.CreateIfNotExists();

                    // Set the permissions so the blobs are public. 
                    BlobContainerPermissions permissions = new BlobContainerPermissions
                    {
                        PublicAccess = BlobContainerPublicAccessType.Blob
                    };
                    await cloudBlobContainer.SetPermissionsAsync(permissions);

                    // Get a reference to the blob address, then upload the file to the blob.
                    CloudBlockBlob cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference(filename);

                    if (stream != null)
                    {
                        await cloudBlockBlob.UploadFromStreamAsync(stream);
                    }
                    else
                    {
                        return (false, null);
                    }

                    return (true, cloudBlockBlob.SnapshotQualifiedStorageUri.PrimaryUri.ToString());
                }
                catch (StorageException ex)
                {
                    Debug.WriteLine("Blob failure: " + ex.ToString());
                    return (false, null);
                }
                finally
                {
                    // TODO: Cleanup
                }
            }
            else
            {
                return (false, null);
            }

        }
    }
}
