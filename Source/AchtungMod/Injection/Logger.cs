using System;
using Verse;

namespace AchtungMod
{
	public class Logger
	{
		public enum Level
		{
			Error,
			Warning,
			Info,
			Debug
		}

		public Level Verbosity;

		public string MessagePrefix;

		public void Debug(string message)
		{
			if (Verbosity < Level.Debug) return;
			Log.Message(MessagePrefix + message);
		}

		public void Debug(string message, params object[] args)
		{
			if (Verbosity < Level.Debug) return;
			Log.Message(String.Format(MessagePrefix + message, args));
		}

		public void Info(string message)
		{
			if (Verbosity < Level.Info) return;
			Log.Message(MessagePrefix + message);
		}

		public void Info(string message, params object[] args)
		{
			if (Verbosity < Level.Info) return;
			Log.Message(String.Format(MessagePrefix + message, args));
		}

		public void Warning(string message)
		{
			if (Verbosity < Level.Warning) return;
			Log.Warning(MessagePrefix + message);
		}

		public void Warning(string message, params object[] args)
		{
			if (Verbosity < Level.Warning) return;
			Log.Warning(String.Format(MessagePrefix + message, args));
		}

		public void Error(string message)
		{
			if (Verbosity < Level.Error) return;
			Log.Error(MessagePrefix + message);
		}

		public void Error(string message, params object[] args)
		{
			if (Verbosity < Level.Error) return;
			Log.Error(String.Format(MessagePrefix + message, args));
		}

	}
}

