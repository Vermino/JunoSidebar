// File: JunoSidebar/JunoSidebar.Wpf/Services/WebService.cs
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf.Services
{
    /// <summary>
    /// Service for handling web API calls to external services.
    /// </summary>
    public class WebService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Initializes a new instance of the WebService class.
        /// </summary>
        public WebService()
        {
            _httpClient = new HttpClient();
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
        }

        /// <summary>
        /// Sends a GET request to the specified URI.
        /// </summary>
        /// <typeparam name="T">The type to deserialize the response content to.</typeparam>
        /// <param name="uri">The URI to send the request to.</param>
        /// <param name="headers">Optional headers to include in the request.</param>
        /// <returns>The deserialized response content.</returns>
        public async Task<T> GetAsync<T>(string uri, params (string Key, string Value)[] headers)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            
            foreach (var (key, value) in headers)
            {
                request.Headers.Add(key, value);
            }
            
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content, _jsonOptions);
        }

        /// <summary>
        /// Sends a POST request with JSON content to the specified URI.
        /// </summary>
        /// <typeparam name="TRequest">The type of the request content.</typeparam>
        /// <typeparam name="TResponse">The type to deserialize the response content to.</typeparam>
        /// <param name="uri">The URI to send the request to.</param>
        /// <param name="data">The request content to send.</param>
        /// <param name="headers">Optional headers to include in the request.</param>
        /// <returns>The deserialized response content.</returns>
        public async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
            string uri, 
            TRequest data, 
            params (string Key, string Value)[] headers)
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = content
            };
            
            foreach (var (key, value) in headers)
            {
                request.Headers.Add(key, value);
            }
            
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<TResponse>(responseContent, _jsonOptions);
        }

        /// <summary>
        /// Sends a POST request with JSON content to the specified URI without expecting a response.
        /// </summary>
        /// <typeparam name="TRequest">The type of the request content.</typeparam>
        /// <param name="uri">The URI to send the request to.</param>
        /// <param name="data">The request content to send.</param>
        /// <param name="headers">Optional headers to include in the request.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task PostJsonAsync<TRequest>(
            string uri, 
            TRequest data, 
            params (string Key, string Value)[] headers)
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = content
            };
            
            foreach (var (key, value) in headers)
            {
                request.Headers.Add(key, value);
            }
            
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Sends a POST request with file content to the specified URI.
        /// </summary>
        /// <typeparam name="TResponse">The type to deserialize the response content to.</typeparam>
        /// <param name="uri">The URI to send the request to.</param>
        /// <param name="filePath">The path to the file to send.</param>
        /// <param name="fileName">The name of the file to use in the request.</param>
        /// <param name="formFieldName">The name of the form field to use for the file.</param>
        /// <param name="headers">Optional headers to include in the request.</param>
        /// <returns>The deserialized response content.</returns>
        public async Task<TResponse> PostFileAsync<TResponse>(
            string uri, 
            string filePath, 
            string fileName, 
            string formFieldName = "file", 
            params (string Key, string Value)[] headers)
        {
            using var formContent = new MultipartFormDataContent();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var fileContent = new StreamContent(fileStream);
            
            formContent.Add(fileContent, formFieldName, fileName);
            
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = formContent
            };
            
            foreach (var (key, value) in headers)
            {
                request.Headers.Add(key, value);
            }
            
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<TResponse>(responseContent, _jsonOptions);
        }

        /// <summary>
        /// Downloads a file from the specified URI.
        /// </summary>
        /// <param name="uri">The URI to download the file from.</param>
        /// <param name="outputPath">The path to save the downloaded file to.</param>
        /// <param name="headers">Optional headers to include in the request.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task DownloadFileAsync(
            string uri, 
            string outputPath, 
            params (string Key, string Value)[] headers)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            
            foreach (var (key, value) in headers)
            {
                request.Headers.Add(key, value);
            }
            
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            
            await contentStream.CopyToAsync(fileStream);
        }
    }
}