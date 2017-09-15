﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Util;
using SharpRaven;
using SharpRaven.Data;

namespace SimCivil.Profile
{
    public class SentryTag
    {
        public string Name { get; set; }
        public IRawLayout Layout { get; set; }
    }

    public class SentryAppender : AppenderSkeleton
    {
        protected IRavenClient RavenClient;
        public string DSN { get; set; }
        public string Logger { get; set; }
        private readonly IList<SentryTag> tagLayouts = new List<SentryTag>();

        public void AddTag(SentryTag tag)
        {
            tagLayouts.Add(tag);
        }

        protected override void Append(LoggingEvent loggingEvent)
        {
            if (RavenClient == null)
            {
                RavenClient = new RavenClient(DSN)
                {
                    Logger = Logger,
                    Release = Assembly.GetExecutingAssembly().GetName().Version.ToString(),

                    // If something goes wrong when sending the event to Sentry, make sure this is written to log4net's internal
                    // log. See <add key="log4net.Internal.Debug" value="true"/>
                    ErrorOnCapture = ex => LogLog.Error(typeof(SentryAppender), "[" + Name + "] " + ex.Message, ex),
                };
            }

            var tags = tagLayouts.ToDictionary(t => t.Name, t => (t.Layout.Format(loggingEvent) ?? "").ToString());

            Exception exception = loggingEvent.ExceptionObject ?? loggingEvent.MessageObject as Exception;
            ErrorLevel level = Translate(loggingEvent.Level);

            SentryEvent sentryEvent;
            if (loggingEvent.ExceptionObject != null)
            {
                // We should capture buth the exception and the message passed
                sentryEvent = new SentryEvent(exception);
            }
            else if (loggingEvent.MessageObject is Exception)
            {
                // We should capture the exception with no custom message
                sentryEvent = new SentryEvent((Exception) loggingEvent.MessageObject);
            }
            else
            {
                // Just capture message
                var message = loggingEvent.RenderedMessage;
                sentryEvent = new SentryEvent(new SentryMessage(message, level, tags));
            }
            sentryEvent.Extra = new
            {
                Environment = new EnvironmentExtra()
            };
            if (loggingEvent.Level.DisplayName == "ERROR" || loggingEvent.Level.DisplayName == "FETAL")
            {
                RavenClient.Capture(sentryEvent);
            }
            else
            {
                RavenClient.CaptureAsync(sentryEvent);
            }
        }

        public static ErrorLevel Translate(Level level)
        {
            switch (level.DisplayName)
            {
                case "WARN":
                    return ErrorLevel.Warning;

                case "NOTICE":
                    return ErrorLevel.Info;
            }

            return !Enum.TryParse(level.DisplayName, true, out ErrorLevel errorLevel)
                ? ErrorLevel.Error
                : errorLevel;
        }

        protected override void Append(LoggingEvent[] loggingEvents)
        {
            foreach (var loggingEvent in loggingEvents)
            {
                Append(loggingEvent);
            }
        }
    }
}