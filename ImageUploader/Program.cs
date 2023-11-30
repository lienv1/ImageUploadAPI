using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.IO;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;

namespace ImageUploader
{
    internal class Program
    {

        private const string configurationFile = "appsettings.json";

        private static string imageSourcePath = @"";

        //IAM
        private static string iamUrl = "";
        private static string iamUsername = "";
        private static string iamPassword = "";
        private static string iamRealm = "";
        private static string iamClientId = "";

        //Fileserver API
        private static string fileserverUrl = "http://localhost:3000";

        static void Main(string[] args)
        {

            Console.WriteLine("Start ...");

            if (!File.Exists(configurationFile)) {
                Console.WriteLine("can not find file: " + configurationFile);
                return;
            }

            ReadConfigurationFile();

            Console.WriteLine("Check if image path exist");
            if (!Directory.Exists(imageSourcePath))
            {
                Console.WriteLine("Can not find path " + imageSourcePath);
                Console.ReadLine();
                return;
            }

            Console.WriteLine("Get all local pictures");
            string [] localPictures = getAllLocalPictures(imageSourcePath);

            Console.WriteLine("Login to IAM");
            string token = GetBearerTokenAsyncMaster(iamUsername, iamPassword, iamClientId, iamRealm, iamUrl).Result;
            
            if (token == null)
            {
                Console.WriteLine("No token");
                Console.ReadLine();
                return;
            }

            List<string> picturesByGetRequest = new List<string>();
            Console.WriteLine("Get all pictures by get request");
            picturesByGetRequest = GetAllPicturesInFileserver(fileserverUrl+"/images/products").Result;

            foreach (string picture in localPictures)
            {
                if (PictureAlreadyExist(picture, picturesByGetRequest))
                    continue;
                string fullPathImage = Path.Combine(imageSourcePath, picture);
                string uploadOriginalUrl = fileserverUrl + "/images/products";
                bool success = UploadImageByPostRequest(fullPathImage, token, uploadOriginalUrl).Result;
                if (!success)
                {
                    Console.WriteLine("Error with " + fullPathImage);
                    break;
                }
                string uploadThumbnailUrl = fileserverUrl+ "/images/products_tn";
                string thumbnail = ConvertToThumbnail(imageSourcePath, picture);
                Console.WriteLine(thumbnail);
                success = UploadImageByPostRequest(thumbnail, token, uploadThumbnailUrl).Result;
               
                if (!success)
                {
                    Console.WriteLine("Error with " + fullPathImage);
                    break;
                }
                string thumbnailFullpath = Path.Combine(imageSourcePath, thumbnail);
                if (File.Exists(thumbnailFullpath))
                    File.Delete(thumbnailFullpath);
            }



            Console.WriteLine("Finished!");
            Console.ReadLine();
        }

        private static void ReadConfigurationFile()
        {
            string jsonText = File.ReadAllText(configurationFile);
            var config = JObject.Parse(jsonText);

            imageSourcePath = config["ImageSourcePath"].ToString();

            // Load IAM configuration
            var iamConfig = config["IAM"];
            iamUrl = iamConfig["Url"].ToString();
            iamUsername = iamConfig["Username"].ToString();
            iamPassword = iamConfig["Password"].ToString();
            iamRealm = iamConfig["Realm"].ToString();
            iamClientId = iamConfig["ClientId"].ToString();

            // Load Fileserver API configuration
            var fileserverApiConfig = config["FileserverApi"];
            fileserverUrl = fileserverApiConfig["Url"].ToString();
        }

        //IAM
        private static async Task<string> GetBearerTokenAsyncMaster(string iamUser, string iamPassword, string iamClientId, string iamRealm, string iamUrl)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                var tokenUrl = iamUrl + "/realms/"+ iamRealm + "/protocol/openid-connect/token";
                var tokenData = "client_id=" + iamClientId + "&username=" + iamUser + "&password=" + iamPassword + "&grant_type=password&scope=openid";
                Console.WriteLine(tokenUrl);
                var content = new StringContent(tokenData, Encoding.UTF8, "application/x-www-form-urlencoded");
                var response = await httpClient.PostAsync(tokenUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Failed to obtain the bearer token. Status code: {response.StatusCode}");
                    return null;
                }

                var token = JsonConvert.DeserializeObject<dynamic>(responseContent)?.access_token;
                return token;
            }
        }

        //Fileserver
        private static async Task<bool> UploadImageByPostRequest(string imagePath,  string token, string url)
        {
            using (var httpClient = new HttpClient())
            {
                // Set the bearer token
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                // Create a MultipartFormDataContent to add the file
                using (var content = new MultipartFormDataContent())
                {
                    // Read the file into a byte array
                    byte[] fileBytes = File.ReadAllBytes(imagePath);
                    var fileContent = new ByteArrayContent(fileBytes);

                    // Add headers to the file content for the server to understand the file
                    fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
                    {
                        Name = "\"image\"", // The name of the form field in your API
                        FileName = "\"" + Path.GetFileName(imagePath) + "\""
                    };
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                    // Add the file content to the request
                    content.Add(fileContent);

                    try
                    {
                        // Make the POST request
                        HttpResponseMessage response = await httpClient.PostAsync(url, content);
                        Console.WriteLine("making request to " + url);
                        if (response.IsSuccessStatusCode)
                        {
                            Console.WriteLine("Image uploaded successfully.");
                            return true;
                        }
                        else
                        {
                            Console.WriteLine($"Failed to upload. Status Code: {response.StatusCode}");
                            return false;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error: " + e.Message);
                        return false;
                    }
                }
            }
        }

        private static async Task<List<string>> GetAllPicturesInFileserver(string fileserverUrl)
        {
            List<string> pictures = new List<string>();
            using (HttpClient httpClient = new HttpClient())
            {
                try
                {
                    HttpResponseMessage response = await httpClient.GetAsync(fileserverUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        JObject jsonResponse = JObject.Parse(content);
                        JArray files = (JArray)jsonResponse["files"];

                        foreach (var file in files)
                        {
                            pictures.Add(file.ToString());
                        }
                    }
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine("Error: " + e.Message);
                }
            }
            return pictures;
        }
        
        //Local
        private static string [] getAllLocalPictures(string imagePath)
        {
             return Directory.GetFiles(imagePath, "*.jpg").Where(file => FilenameHasPattern(file)).Select(Path.GetFileName).ToArray();
        }

        //Tools
        private static string ConvertToThumbnail(string imagepath, string picture)
        {
            string thumbnail = "tn_" + picture;

            float sizeDifferenceOfHeight;
            float autoWidth;
            float fixedHeight = 300f;

            Image image = Image.FromFile(Path.Combine(imagepath, picture));

            sizeDifferenceOfHeight = (fixedHeight / image.Height) * 1000f; //percentage of quality loss,
            autoWidth = image.Width / 1000f * sizeDifferenceOfHeight;

            Size size = new Size((int)autoWidth, 300);
            Image newImage = new Bitmap(image, size);
            newImage.Save(Path.Combine(imagepath, thumbnail)); // "tn_" + picture
            return Path.Combine(imagepath,thumbnail);
        }

        private static bool FilenameHasPattern(string picture)
        {
            picture = Path.GetFileName(picture);

            if (!picture.Contains(".")) 
                return false;

            string pictureNameWithoutExtension = picture.Split('.')[0];
            if (pictureNameWithoutExtension.Length != 5)
                return false;

            bool isNumeric = int.TryParse(pictureNameWithoutExtension, out _);

            if (!isNumeric)
                return false;

            return true;
        }

        private static bool PictureAlreadyExist(string picture, List<string> listOfFiles)
        {
            foreach (string filename in listOfFiles)
            {
                if (picture == filename)
                    return true;
            }
            return false;
        }
    
    
    
    }
}