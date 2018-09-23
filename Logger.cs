using System;
using System.Collections.Generic;
using Nancy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace RhinoCommon.Rest
{
    class Logger
    {
        public static void Init()
        {
            Log.Logger = new LoggerConfiguration()
#if DEBUG
                .MinimumLevel.Debug()
#endif
                .WriteTo.Console(outputTemplate:
        "{Timestamp:o} {Level:w4}: {Message:j} {Properties:j}{NewLine}{Exception}")
                .CreateLogger();
        }

        public static void Info(NancyContext context, string format, params object[] args)
        {
            Write(context, Serilog.Events.LogEventLevel.Information, format, args);
        }

        public static void Info(NancyContext context, Dictionary<string, string> data)
        {
            Write(context, Serilog.Events.LogEventLevel.Information, data);
        }

        public static void Debug(NancyContext context, string format, params object[] args)
        {
            Write(context, Serilog.Events.LogEventLevel.Debug, format, args);
        }
        public static void Debug(NancyContext context, Dictionary<string, string> data)
        {
            Write(context, Serilog.Events.LogEventLevel.Debug, data);
        }

        public static void Warning(NancyContext context, string format, params object[] args)
        {
            Write(context, Serilog.Events.LogEventLevel.Warning, format, args);
        }
        public static void Warning(NancyContext context, Dictionary<string, string> data)
        {
            Write(context, Serilog.Events.LogEventLevel.Warning, data);
        }

        public static void Error(NancyContext context, string format, params object[] args)
        {
            Write(context, Serilog.Events.LogEventLevel.Error, format, args);
        }
        public static void Error(NancyContext context, Dictionary<string, string> data)
        {
            Write(context, Serilog.Events.LogEventLevel.Error, data);
        }

        static void Write(NancyContext context, Serilog.Events.LogEventLevel severity, string format, params object[] args)
        {
            var data = new Dictionary<string, string>
            {
                { "message", string.Format(format, args) }
            };
            Write(context, severity, data);
        }

        static void Write(NancyContext context, Serilog.Events.LogEventLevel severity, Dictionary<string, string> data)
        {
            var log = new JObject();
            log.Add("dateTime", DateTime.UtcNow.ToString("o")); // ISO 8601 format
            log.Add("severity", severity.ToString());

            foreach(var pair in data)
            {
                log.Add(pair.Key, pair.Value);
            }

            if (context != null)
            {
                object item = null;
                if (context.Request != null)
                {
                    log.Add("sourceIpAddress", context.Request.UserHostAddress);
                    log.Add("path", context.Request.Url.Path);
                    log.Add("query", context.Request.Url.Query);
                    log.Add("method", context.Request.Method);
                }

                if (context.Items != null)
                {
                    if (context.Items.TryGetValue("x-compute-id", out item))
                        log.Add("requestId", item as string);
                    if (context.Items.TryGetValue("x-compute-host", out item))
                        log.Add("computeHost", item as string);
                    if (context.Items.TryGetValue("auth_user", out item))
                        log.Add("auth_user", item as string);
                }
            }

            Log.Write(severity, JsonConvert.SerializeObject(log, Formatting.None));
        }
    }
}
