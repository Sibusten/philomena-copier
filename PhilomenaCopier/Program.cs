using Newtonsoft.Json;
using SemVersion;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PhilomenaCopier {
    public class Program {
        public static readonly SemanticVersion Version = new SemanticVersion(1, 0, 0);

        // Matches a domain, ignoring "http"/"https" and trailing "/"
        private const string DomainPattern = @"^(?:https?:\/\/)?(.+?\..+?)\/?$$";

        // Matches a Philomena API Key. Alphanumeric, 20 characters long.
        private const string ApiKeyPattern = @"^([a-zA-Z0-9]{20})$";

        private const int PerPage = 50;

        private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(1024);  // 17 minutes and 4 seconds

        // A browser user agent
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:77.0) Gecko/20100101 Firefox/77.0";

        private class Image {
            public string description;
            public string source_url;
            public List<string> tags;
            public string view_url;
            public int id;
        }

        private class SearchQueryImages {
            public List<Image> images;
            public int total;
        }

        private class UploadImageInfo {
            public string description;
            public string tag_input;
            public string source_url;
        }

        private class UploadImageBody {
            public UploadImageInfo image;
            public string url;
        }

        private static string GetSearchQueryUrl(string booru, string apiKey, string query, int page) {
            return $"https://{booru}/api/v1/json/search/images?key={apiKey}&page={page}&per_page={PerPage}&q={query}&sf=created_at&sd=asc";
        }

        private static string GetUploadImageUrl(string booru, string apiKey) {
            return $"https://{booru}/api/v1/json/images?key={apiKey}";
        }

        private static string GetInputWithPattern(string pattern, string promptText, string errorText = "Invalid input") {
            while (true) {
                Console.Write(promptText);
                string input = Console.ReadLine().Trim();

                // Check against pattern
                Match match = Regex.Match(input, pattern);
                if (match.Success) {
                    return match.Groups[1].Value;
                }

                Console.WriteLine(errorText);
            }
        }

        private static async Task<SearchQueryImages> GetSearchQueryImages(WebClient wc, string booru, string apiKey, string query, int page) {
            // Set required headers
            wc.Headers["User-Agent"] = UserAgent;

            string queryUrl = GetSearchQueryUrl(booru, apiKey, query, page);
            string searchJson = await wc.DownloadStringTaskAsync(queryUrl);
            return JsonConvert.DeserializeObject<SearchQueryImages>(searchJson);
        }

        private static async Task UploadImage(WebClient wc, Image image, string booru, string apiKey) {
            // Set required headers
            wc.Headers["User-Agent"] = UserAgent;
            wc.Headers["Content-Type"] = "application/json";

            WebHeaderCollection x = wc.Headers;

            string uploadUrl = GetUploadImageUrl(booru, apiKey);

            // Format the tags into a comma-separated string
            string tagString = string.Join(", ", image.tags);

            // Create upload json
            UploadImageInfo uploadImage = new UploadImageInfo
            {
                description = image.description,
                tag_input = tagString,
                source_url = image.source_url
            };
            UploadImageBody uploadImageBody = new UploadImageBody
            {
                image = uploadImage,
                url = image.view_url
            };
            string uploadImageString = JsonConvert.SerializeObject(uploadImageBody);

            await wc.UploadDataTaskAsync(uploadUrl, Encoding.UTF8.GetBytes(uploadImageString));
        }

        public static async Task Main(string[] args) {
            Console.WriteLine($"Philomena Copier v{Version}");
            Console.WriteLine();
            Console.WriteLine("Ensure your filters are set correctly on the source booru. The active filter will be used when copying images.");
            Console.WriteLine("API keys can be found on the Account page.");
            Console.WriteLine();

            // Get booru info
            string sourceBooru = GetInputWithPattern(DomainPattern, "Enter source booru url: ");
            string sourceApiKey = GetInputWithPattern(ApiKeyPattern, "Enter source booru API Key: ");
            string targetBooru = GetInputWithPattern(DomainPattern, "Enter target booru url: ");
            string targetApiKey = GetInputWithPattern(ApiKeyPattern, "Enter target booru API Key: ");

            // Get query
            Console.WriteLine("Enter query to copy from the source booru to the target booru. Any query that can be made on the site will work.");
            Console.Write("Query: ");
            string searchQuery = Console.ReadLine().Trim();

            using (WebClient wc = new WebClient()) {
                // Get the first page of images
                int currentPage = 1;
                SearchQueryImages searchImages = await GetSearchQueryImages(wc, sourceBooru, sourceApiKey, searchQuery, currentPage);

                // Check how many images are in the query
                if (searchImages.total == 0) {
                    Console.WriteLine("This query has no images! Double-check the query and try again.");
                    return;
                }

                Console.WriteLine($"There are {searchImages.total} images in this query");
                Console.WriteLine("Ensure the query and image count are correct! If they are not, Ctrl-C to exit. Otherwise, press enter to continue.");
                Console.ReadLine();

                // Upload all images
                int currentImage = 1;
                TimeSpan currentRetryDelay;
                while (searchImages.images.Count > 0) {
                    // Upload the current page
                    foreach (Image image in searchImages.images) {
                        // Reset the retry delay
                        currentRetryDelay = InitialRetryDelay;

                        bool success = false;

                        while (!success) {
                            try {
                                Console.WriteLine($"Uploading image {currentImage}/{searchImages.total} ({image.id})...");
                                await UploadImage(wc, image, targetBooru, targetApiKey);

                                success = true;
                            }
                            catch (WebException ex) {
                                if (ex.Status == WebExceptionStatus.ProtocolError) {
                                    HttpWebResponse response = ex.Response as HttpWebResponse;
                                    if (response != null) {
                                        if (response.StatusCode == HttpStatusCode.BadRequest) {  // Already uploaded (duplicate hash)
                                            Console.WriteLine("Image has already been uploaded");
                                            success = true;
                                        }
                                        else {
                                            // Other http status code
                                            Console.WriteLine($"Error uploading image ({response.StatusCode})");
                                        }
                                    }
                                    else {
                                        // no http status code available
                                        Console.WriteLine("Error uploading image (Unknown error)");
                                    }
                                }
                                else {
                                    // no http status code available
                                    Console.WriteLine("Error uploading image (Unknown error)");
                                }
                            }

                            if (!success) {
                                // Exponential backoff to prevent overloading server
                                Console.WriteLine($"Retrying in {currentRetryDelay.TotalSeconds} seconds...");
                                await Task.Delay(currentRetryDelay);

                                // Double the delay for next time, if it is below the max
                                if (currentRetryDelay < MaxRetryDelay) {
                                    currentRetryDelay *= 2;
                                }
                            }
                        }

                        currentImage++;

                        // Delay to prevent overloading servers
                        await Task.Delay(InitialRetryDelay);
                    }

                    // Load the next page
                    currentPage++;
                    searchImages = await GetSearchQueryImages(wc, sourceBooru, sourceApiKey, searchQuery, currentPage);
                }
            }
        }
    }
}
