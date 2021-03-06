﻿using System;
using Nancy.Hosting.Self;
using Nancy.Extensions;
using Topshelf;
using Nancy.Conventions;
using Nancy.Bootstrapper;
using Nancy.TinyIoc;
using System.Collections.Generic;

namespace compute.geometry
{
    class Program
    {
        static void Main(string[] args)
        {
            int backendPort = Env.GetEnvironmentInt("COMPUTE_BACKEND_PORT", 8081);

            Topshelf.HostFactory.Run(x =>
            {
                x.ApplyCommandLine();
                x.SetStartTimeout(new TimeSpan(0, 1, 0));
                x.Service<NancySelfHost>(s =>
                  {
                      s.ConstructUsing(name => new NancySelfHost());
                      s.WhenStarted(tc => tc.Start(backendPort));
                      s.WhenStopped(tc => tc.Stop());
                  });
                x.RunAsPrompt();
                //x.RunAsLocalService();
                x.SetDisplayName("compute.geometry");
                x.SetServiceName("compute.geometry");
            });
            RhinoLib.ExitInProcess();
        }
    }

    public class NancySelfHost
    {
        private NancyHost _nancyHost;
        private System.Diagnostics.Process _backendProcess = null;
        public static bool RunningHttps { get; set; }

        public void Start(int http_port)
        {
            Logger.Init();
            Logger.Info(null, $"Launching RhinoCore library as {Environment.UserName}");
            RhinoLib.LaunchInProcess(RhinoLib.LoadMode.Headless, 0);
            var config = new HostConfiguration();
#if DEBUG
            config.RewriteLocalhost = false;  // Don't require URL registration since geometry service always runs on localhost
#endif
            var listenUriList = new List<Uri>();

            if (http_port > 0)
                listenUriList.Add(new Uri($"http://localhost:{http_port}"));

            if (listenUriList.Count > 0)
                _nancyHost = new NancyHost(config, listenUriList.ToArray());
            else
                Logger.Error(null, "Neither http_port nor https_port are set; NOT LISTENING!");
            try
            {
                _nancyHost.Start();
                foreach (var uri in listenUriList)
                    Logger.Info(null, $"compute.geometry server running on {uri.OriginalString}");
            }
            catch (Nancy.Hosting.Self.AutomaticUrlReservationCreationFailureException)
            {
                Logger.Error(null, Environment.NewLine + "URL Not Reserved. From an elevated command promt, run:" + Environment.NewLine);
                foreach (var uri in listenUriList)
                    Logger.Error(null, $"netsh http add urlacl url={uri.Scheme}://+:{uri.Port}/ user=Everyone");
                Environment.Exit(1);
            }
        }

        public void Stop()
        {
            if (_backendProcess != null)
                _backendProcess.Kill();
            _nancyHost.Stop();
        }
    }

    public class Bootstrapper : Nancy.DefaultNancyBootstrapper
    {
        private byte[] _favicon;

        protected override void ApplicationStartup(TinyIoCContainer container, IPipelines pipelines)
        {
            Logger.Debug(null, "ApplicationStartup");
            base.ApplicationStartup(container, pipelines);
        }

        protected override void ConfigureConventions(NancyConventions nancyConventions)
        {
            base.ConfigureConventions(nancyConventions);
            nancyConventions.StaticContentsConventions.Add(StaticContentConventionBuilder.AddDirectory("docs"));
        }

        protected override byte[] FavIcon
        {
            get { return _favicon ?? (_favicon = LoadFavIcon()); }
        }

        private byte[] LoadFavIcon()
        {
            using (var resourceStream = GetType().Assembly.GetManifestResourceStream("compute.geometry.favicon.ico"))
            {
                var memoryStream = new System.IO.MemoryStream();
                resourceStream.CopyTo(memoryStream);
                return memoryStream.GetBuffer();
            }
        }
    }

    public class RhinoModule : Nancy.NancyModule
    {
        public RhinoModule()
        {
            Get["/healthcheck"] = _ => "healthy";

            var endpoints = EndPointDictionary.GetDictionary();
            foreach (var kv in endpoints)
            {
                Get[kv.Key] = _ =>
                {
                    if (NancySelfHost.RunningHttps && !Request.Url.IsSecure)
                    {
                        string url = Request.Url.ToString().Replace("http", "https");
                        return new Nancy.Responses.RedirectResponse(url, Nancy.Responses.RedirectResponse.RedirectType.Permanent);
                    }
                    var response = kv.Value.HandleGetAsResponse(Context);
                    if (response != null)
                        return response;
                    return kv.Value.HandleGet();
                };

                if (kv.Value is GetEndPoint)
                    continue;

                Post[kv.Key] = _ =>
                {
                    if (NancySelfHost.RunningHttps && !Request.Url.IsSecure)
                        return Nancy.HttpStatusCode.HttpVersionNotSupported;

                    // Stashing middleware may have already read the body
                    object requestBody = null;
                    string jsonString = null;
                    if (Context.Items.TryGetValue("request-body", out requestBody))
                        jsonString = requestBody as string;
                    else
                        jsonString = Request.Body.AsString();

                    var resp = new Nancy.Response();
                    resp.Contents = (e) =>
                    {
                        using (var sw = new System.IO.StreamWriter(e))
                        {
                            bool multiple = false;
                            Dictionary<string, string> returnModifiers = null;
                            foreach (string name in Request.Query)
                            {
                                if (name.StartsWith("return.", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    if (returnModifiers == null)
                                        returnModifiers = new Dictionary<string, string>();
                                    string dataType = "Rhino.Geometry." + name.Substring("return.".Length);
                                    string items = Request.Query[name];
                                    returnModifiers[dataType] = items;
                                    continue;
                                }
                                if (name.Equals("multiple", StringComparison.InvariantCultureIgnoreCase))
                                    multiple = Request.Query[name];
                            }
                            var postResult = kv.Value.HandlePost(jsonString, multiple, returnModifiers);
                            sw.Write(postResult);
                            sw.Flush();
                        }
                    };
                    return resp;
                };
            }
        }

    }
}
