﻿using System;
using Nancy;
using Nancy.Bootstrapper;
using Nancy.Extensions;
using Newtonsoft.Json.Linq;
using Amazon.S3;
using Serilog;


namespace RhinoCommon.Rest
{
    public enum StashProviders
    {
        None = 0,
        TempFile = 1,
        AmazonS3 = 2
    }

    public static class RequestStashing
    {
        static bool _s3bucket_created = false;

        public static void AddRequestStashing(this IPipelines pipelines)
        {
            if (Env.GetEnvironmentBool("COMPUTE_STASH_TEMPFILE", false))
            {
                pipelines.BeforeRequest += TempFileStasher;
                Log.Information("Request stashing enabled via TempFileStasher");
            }
            if (Env.GetEnvironmentBool("COMPUTE_STASH_AMAZONS3", true))
            {
                pipelines.BeforeRequest += AmazonS3RequestStasher;
                Log.Information("Request stashing enabled via AmazonS3RequestStasher");
            }
        }
        public static Response AmazonS3RequestStasher(NancyContext context)
        {
            if (context.Request.Method != "POST")
                return null;

            object request_id = null;
            if (!context.Items.TryGetValue("RequestId", out request_id))
            {
                return null;
            }

            var bucket = Environment.GetEnvironmentVariable("COMPUTE_STASH_S3_BUCKET");
            if (string.IsNullOrWhiteSpace(bucket))
            {
                Log.ForContext("RequestId", context.Items["RequestId"])
                    .Warning("COMPUTE_STASH_S3_BUCKET not set");
                return null;
            }

            
            var client = new AmazonS3Client(Amazon.RegionEndpoint.USEast1);
            if (!_s3bucket_created)
            {
                var pbr = new Amazon.S3.Model.PutBucketRequest();
                pbr.BucketName = bucket;
                pbr.UseClientRegion = true;
                client.PutBucket(pbr);
                _s3bucket_created = true;
            }

            var por = new Amazon.S3.Model.PutObjectRequest();
            por.Key = request_id as string;
            por.BucketName = bucket;
            por.ContentBody = GetRequestJson(context);
            client.PutObjectAsync(por);

            return null;
        }

        static string GetRequestJson(NancyContext context)
        {
            var body = context.Request.Body.AsString();

            var request = new JObject();
            request.Add("body", body);  // Do not assume that the body is valid, parsable JSON; save it as it arrives.
            request.Add("path", context.Request.Url.Path);
            request.Add("query", context.Request.Url.Query);
            object auth_user = null;
            if (context.Items.TryGetValue("auth_user", out auth_user))
                request.Add("auth_user", auth_user as string);
            var headers = new JObject();
            foreach (var header in context.Request.Headers)
            {
                var headerValues = new JObject();
                headers.Add(header.Key, JToken.FromObject(header.Value));
            }
            request.Add("headers", headers);
            return request.ToString();
        }

        public static Response TempFileStasher(NancyContext context)
        {
            if (context.Request.Method != "POST")
                return null;

            var stashDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Compute", "Requests");
            if (!System.IO.Directory.Exists(stashDir))
                System.IO.Directory.CreateDirectory(stashDir);

            string filename = System.IO.Path.Combine(stashDir, string.Format("{0}.request.log", context.Items["RequestId"]));

            System.IO.File.WriteAllText(filename, GetRequestJson(context));

            return null;
        }
    }
}
