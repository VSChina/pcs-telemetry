﻿// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Diagnostics;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Exceptions;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Http;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Models;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Runtime;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.StorageAdapter;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services
{
    public interface IStorageAdapterClient
    {
        Task<Tuple<bool, string>> PingAsync();
        Task<ValueListApiModel> GetAllAsync(string collectionId);
        Task<ValueApiModel> GetAsync(string collectionId, string key);
        Task<ValueApiModel> CreateAsync(string collectionId, string value);
        Task<ValueApiModel> UpdateAsync(string collectionId, string key, string value, string etag);
        Task DeleteAsync(string collectionId, string key);
    }

    // TODO: handle retriable errors
    public class StorageAdapterClient : IStorageAdapterClient
    {
        // TODO: make it configurable, default to false
        private const bool AllowInsecureSSLServer = true;

        private readonly IHttpClient httpClient;
        private readonly ILogger log;
        private readonly string serviceUri;
        private readonly int timeout;

        public StorageAdapterClient(
            IHttpClient httpClient,
            IServicesConfig config,
            ILogger logger)
        {
            this.httpClient = httpClient;
            this.log = logger;
            this.serviceUri = config.StorageAdapterApiUrl;
            this.timeout = config.StorageAdapterApiTimeout;
        }

        public async Task<Tuple<bool, string>> PingAsync()
        {
            try
            {
                var response = await httpClient.GetAsync(
                    this.PrepareRequest($"status"));

                if (response.IsError)
                {
                    return new Tuple<bool, string>(false, "Status code: " + response.StatusCode);
                }

                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(response.Content);
                if (data["Status"].ToString().StartsWith("OK:"))
                {
                    return new Tuple<bool, string>(true, data["Status"].ToString());
                }

                return new Tuple<bool, string>(false, data["Status"].ToString());
            }
            catch (Exception e)
            {
                this.log.Error("Storage adapter check failed", () => new { e });
                return new Tuple<bool, string>(false, e.Message);
            }
        }

        public async Task<ValueListApiModel> GetAllAsync(string collectionId)
        {
            var response = await httpClient.GetAsync(
                this.PrepareRequest($"collections/{collectionId}/values"));

            ThrowIfError(response, collectionId, "");

            return JsonConvert.DeserializeObject<ValueListApiModel>(response.Content);
        }

        public async Task<ValueApiModel> GetAsync(string collectionId, string key)
        {
            var response = await httpClient.GetAsync(
                this.PrepareRequest($"collections/{collectionId}/values/{key}"));

            ThrowIfError(response, collectionId, key);

            return JsonConvert.DeserializeObject<ValueApiModel>(response.Content);
        }

        public async Task<ValueApiModel> CreateAsync(string collectionId, string value)
        {
            var response = await httpClient.PostAsync(
                this.PrepareRequest($"collections/{collectionId}/values",
                    new ValueApiModel { Data = value }));

            ThrowIfError(response, collectionId, "");

            return JsonConvert.DeserializeObject<ValueApiModel>(response.Content);
        }

        public async Task<ValueApiModel> UpdateAsync(string collectionId, string key, string value, string etag)
        {
            var response = await httpClient.PutAsync(
                this.PrepareRequest($"collections/{collectionId}/values/{key}",
                    new ValueApiModel { Data = value, ETag = etag }));

            ThrowIfError(response, collectionId, key);

            return JsonConvert.DeserializeObject<ValueApiModel>(response.Content);
        }

        public async Task DeleteAsync(string collectionId, string key)
        {
            var response = await httpClient.DeleteAsync(
                this.PrepareRequest($"collections/{collectionId}/values/{key}"));

            ThrowIfError(response, collectionId, key);
        }

        private HttpRequest PrepareRequest(string path, ValueApiModel content = null)
        {
            var request = new HttpRequest();
            request.AddHeader(HttpRequestHeader.Accept.ToString(), "application/json");
            request.AddHeader(HttpRequestHeader.CacheControl.ToString(), "no-cache");
            request.AddHeader(HttpRequestHeader.UserAgent.ToString(), "Device Simulation " + this.GetType().FullName);
            request.SetUriFromString($"{this.serviceUri}/{path}");
            request.Options.EnsureSuccess = false;
            request.Options.Timeout = this.timeout;
            if (this.serviceUri.ToLowerInvariant().StartsWith("https:"))
            {
                request.Options.AllowInsecureSSLServer = AllowInsecureSSLServer;
            }

            if (content != null)
            {
                request.SetContent(content);
            }

            return request;
        }

        private void ThrowIfError(IHttpResponse response, string collectionId, string key)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ResourceNotFoundException($"Resource {collectionId}/{key} not found.");
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                throw new ConflictingResourceException(
                    $"Resource {collectionId}/{key} out of date. Reload the resource and retry.");
            }

            if (response.IsError)
            {
                throw new ExternalDependencyException(
                    new HttpRequestException($"Storage request error: status code {response.StatusCode}"));
            }
        }
    }
}