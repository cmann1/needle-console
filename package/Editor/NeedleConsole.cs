using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace Needle.Console
{
	public static class NeedleConsole
	{
		private static readonly Regex namespaceCompactNoReturnTypeRegex = new Regex(@"(.+?)\(", RegexOptions.Compiled);
		private static readonly Regex namespaceCompactRegex = new Regex(@" ([^ ]+?)\(", RegexOptions.Compiled);
		private static readonly Regex paramsRegex = new Regex(@"\((?!at)(.*?)\)", RegexOptions.Compiled);
		private static readonly Regex paramsArgumentRegex = new Regex(@"([ (])([^),]+?) (.+?)([\),])", RegexOptions.Compiled);
		private static readonly Regex paramsRefRegex = new Regex(@"\b ?ref ?\b", RegexOptions.Compiled);
		private static readonly MatchEvaluator namespaceReplacer = NamespaceReplacer;
		private static readonly MatchEvaluator paramReplacer = ParamReplacer;

		[HyperlinkCallback(Href = "OpenNeedleConsoleSettings")]
		private static void OpenNeedleConsoleUserPreferences()
		{
			SettingsService.OpenUserPreferences("Preferences/Needle/Console");
		}
		
		[InitializeOnLoadMethod]
		private static void Init()
		{
			var projectSettings = NeedleConsoleProjectSettings.instance;
			var settings = NeedleConsoleSettings.instance;
			if (projectSettings.FirstInstall)
			{
				async void InstalledLog()
				{
					await Task.Delay(100);
					Enable();
					projectSettings.FirstInstall = false;
					projectSettings.Save();
					Debug.Log(
						$"Thanks for installing Needle Console. You can find Settings under <a href=\"OpenNeedleConsoleSettings\">Edit/Preferences/Needle/Console</a>\n" +
						$"If you discover issues please report them <a href=\"https://github.com/needle-tools/needle-console/issues\">on github</a>\n" +
						$"Also feel free to join <a href=\"https://discord.gg/CFZDp4b\">our discord</a>");
				}

				InstalledLog();
				InstallDefaultTheme();
				async void InstallDefaultTheme()
				{
					while (true)
					{
						try
						{
							if (settings.SetDefaultTheme()) break;
						}
						catch
						{
							// ignore
						}
						await Task.Delay(1_000);
					}
				}
			}

			if (settings.CurrentTheme != null)
			{
				settings.CurrentTheme.EnsureEntries();
				settings.CurrentTheme.SetActive();
			}
		}

		public static void Enable()
		{
			NeedleConsoleSettings.instance.Enabled = true;
			NeedleConsoleSettings.instance.Save();
			Patcher.ApplyPatches();
		}

		public static void Disable()
		{
			NeedleConsoleSettings.instance.Enabled = false;
			NeedleConsoleSettings.instance.Save();
			Patcher.RemovePatches();
		}

		private static readonly StringBuilder builder = new StringBuilder();
		internal static string DemystifyEndMarker = "�";

		public static void Apply(ref string stacktrace)
		{
			try
			{
				using (new ProfilerMarker("Needle Console.Apply").Auto())
				{
					if(Profiler.enabled) return;
					
					string[] lines = null;
					using (new ProfilerMarker("Split Lines").Auto())
						lines = stacktrace.Split('\n');
					var settings = NeedleConsoleSettings.instance;
					var foundPrefix = false;
					var foundEnd = false;
					foreach (var t in lines)
					{
						var line = t;
						if (line == DemystifyEndMarker)
						{
							foundEnd = true;
							builder.AppendLine();
							continue;
						}

						if (foundEnd)
						{
							builder.AppendLine(line);
							continue;
						}

						using (new ProfilerMarker("Remove Markers").Auto())
						{
							if (StacktraceMarkerUtil.IsPrefix(line))
							{
								StacktraceMarkerUtil.RemoveMarkers(ref line);
								if (!string.IsNullOrEmpty(settings.Separator))
									builder.AppendLine(settings.Separator);
								foundPrefix = true;
							}
						}

						if (foundPrefix)
						{
							if (settings.StacktraceNamespaceMode == NeedleConsoleSettings.StacktraceNamespace.Compact)
								line = namespaceCompactRegex.Replace(line, namespaceReplacer);
							else if (settings.StacktraceNamespaceMode == NeedleConsoleSettings.StacktraceNamespace.CompactNoReturnType)
								line = namespaceCompactNoReturnTypeRegex.Replace(line, namespaceReplacer);

							if (settings.StacktraceParamsMode != NeedleConsoleSettings.StacktraceParams.Full)
							{
								line = paramsRefRegex.Replace(line, "");
								line = paramsRegex.Replace(line, paramReplacer);
							}
						}

						if (foundPrefix && settings.UseSyntaxHighlighting)
							SyntaxHighlighting.AddSyntaxHighlighting(ref line);

						var l = line.Trim();
#if UNITY_6000_0_OR_NEWER
						// Indent wrapped lines.
						l = "<indent=0.75em><line-indent=-0.75em>" + l + "</line-indent></indent>";
#endif
						if (!string.IsNullOrEmpty(l))
						{
							if (!l.EndsWith("\n"))
								builder.AppendLine(l);
							else
								builder.Append(l);
						}
					}

					var res = builder.ToString();
					if (!string.IsNullOrWhiteSpace(res))
					{
						stacktrace = res;
					}

					builder.Clear();
				}
			}
			catch
				// (Exception e)
			{
				// ignore
			}
		}

		private static string NamespaceReplacer(Match match)
		{
			var lastDotIndex = match.Value.LastIndexOf(".", StringComparison.Ordinal);
			if (lastDotIndex == -1)
				return match.Value;

			// Remove everything but the last two parts leaving only "Class.Method". 
			var secondLastDotIndex = match.Value.LastIndexOf(".", lastDotIndex - 1, StringComparison.Ordinal);
			var result = secondLastDotIndex != -1
				? match.Value[(secondLastDotIndex + 1)..]
				: match.Value[(lastDotIndex + 1)..];

			var plusIndex = result.LastIndexOf("+", StringComparison.Ordinal);
			return plusIndex != -1
				? " " + result[(plusIndex + 1)..]
				: " " + result;
		}

		private static string ParamReplacer(Match match)
		{
			return NeedleConsoleSettings.instance.StacktraceParamsMode switch
			{
				NeedleConsoleSettings.StacktraceParams.TypesOnly => paramsArgumentRegex.Replace(match.Value, "$1$2$4"),
				NeedleConsoleSettings.StacktraceParams.NamesOnly => paramsArgumentRegex.Replace(match.Value, "$1$3$4"),
				NeedleConsoleSettings.StacktraceParams.Compact => "()",
				_ => throw new ArgumentOutOfRangeException(),
			};
		}
	}
}
