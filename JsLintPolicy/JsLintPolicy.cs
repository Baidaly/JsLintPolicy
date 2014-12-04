using System;
using System.Diagnostics;
using System.Windows.Forms;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Microsoft.TeamFoundation.VersionControl.Client;
using System.Text.RegularExpressions;
using System.IO;
using JTC.SharpLinter;
using JTC.SharpLinter.Config;

[Serializable]
public class JsLintPolicy : PolicyBase
{
    private string _outputFormat;
    public string OutputFormat
    {
        get
        {
            if (String.IsNullOrEmpty(_outputFormat))
            {
                return "{0}({1}): ({2}) {3} {4}";
            }
            else
            {
                return _outputFormat;
            }
        }

        set
        {
            _outputFormat = value;
        }
    }

    public override string Description
    {
        get { return "JSLint check-in policy"; }
    }

    // This is a string that is stored with the policy definition on the source
    // control server. If a user does not have the policy plug-in installed, this string
    // is displayed.  You can use this to explain to the user how they should 
    // install the policy plug-in.
    public override string InstallationInstructions
    {
        get { return "To install this policy, read InstallInstructions.txt."; }
    }

    // This string identifies the type of policy. It is displayed in the 
    // policy list when you add a new policy to a Team Project.
    public override string Type
    {
        get { return "JSLint Policy"; }
    }

    // This string is a description of the type of policy. It is displayed 
    // when you select the policy in the Add Check-in Policy dialog box.
    public override string TypeDescription
    {
        get { return "Checks all JS files using JSLint"; }
    }

    // This method is called by the policy framework when you create 
    // a new check-in policy or edit an existing check-in policy.
    // You can use this to display a UI specific to this policy type 
    // allowing the user to change the parameters of the policy.
    public override bool Edit(IPolicyEditArgs args)
    {
        // Do not need any custom configuration
        return true;
    }

    public override PolicyFailure[] Evaluate()
    {
        JsLintConfiguration finalConfig = new JsLintConfiguration();
        SharpLinter lint = new SharpLinter(finalConfig);
        List<JsLintData> allErrors = new List<JsLintData>();

        StringCollection policyFailureMessages = new StringCollection();

        PendingChange[] changes = PendingCheckin.PendingChanges.CheckedPendingChanges;
        foreach (PendingChange pendingChange in changes)
        {
            if ((pendingChange.ChangeType.HasFlag(ChangeType.Add) ||
                  pendingChange.ChangeType.HasFlag(ChangeType.Edit)) &&
                  pendingChange.FileName.EndsWith(".js") && !ShouldIgnore(pendingChange.LocalItem))
            {
                allErrors.AddRange(LintFile(pendingChange.LocalItem, lint));
            }
        }

        if (allErrors.Count > 0)
        {
            List<PolicyFailure> policyFailures = new List<PolicyFailure>();

            foreach (JsLintData error in allErrors)
            {
                string character = error.Character.ToString();
                character = error.Character >= 0 ? "at character " + error.Character : String.Empty;
                policyFailures.Add(new PolicyFailure(string.Format(OutputFormat, error.FilePath, error.Line, error.Source, error.Reason, character)));
                //Console.WriteLine(string.Format(OutputFormat, error.FilePath, error.Line, error.Source, error.Reason, character));
            }

            //List<PolicyFailure> policyFailures = new List<PolicyFailure>();

            //foreach (string message in policyFailureMessages)
            //{
            //    policyFailures.Add(new PolicyFailure(message, this));
            //}

            return policyFailures.ToArray();
        }
        else
        {
            return new PolicyFailure[0];
        }
    }

    // This method is called if the user double-clicks on 
    // a policy failure in the UI. In this case a message telling the user 
    // to supply some comments is displayed.
    public override void Activate(PolicyFailure failure)
    {
        MessageBox.Show("Please fix all JS errors for your check-in.", "JS errors");
    }

    // This method is called if the user presses F1 when a policy failure 
    // is active in the UI. In this example, a message box is displayed.
    public override void DisplayHelp(PolicyFailure failure)
    {
        MessageBox.Show("Please fix all JS errors for your check-in.", "Prompt Policy Help");
    }

    public static bool NotJsOrMinifiedOrDocumentOrNotExists(string file)
    {
        return !Path.GetExtension(file).Equals(".js", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith(".debug.js", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith(".intellisense.js", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith("_references.js", StringComparison.OrdinalIgnoreCase) ||
        file.Contains("-vsdoc.js") ||
        !File.Exists(file);
    }

    public List<JsLintData> LintFile(string file, SharpLinter lint)
    {
        List<JsLintData> fileErrors = new List<JsLintData>();
        string javascript = File.ReadAllText(file);
        JsLintResult result = lint.Lint(javascript);
        bool hasErrors = result.Errors.Count > 0;

        if (hasErrors)
        {
            foreach (JsLintData error in result.Errors)
            {
                error.FilePath = file;
                fileErrors.Add(error);
            }
        }

        SharpCompressor compressor = new SharpCompressor();

        // We always check for YUI errors when there were no lint errors and
        // Otherwise it might not compress.
        if (!hasErrors)
        {
            compressor.Clear();
            compressor.AllowEval = true;
            //compressor.KeepHeader = Configuration.MinimizeKeepHeader;
            //compressor.CompressorType = Configuration.CompressorType;

            hasErrors = !compressor.YUITest(javascript);

            if (hasErrors)
            {
                foreach (var error in compressor.Errors)
                {
                    fileErrors.Add(error);
                }
            }
        }

        fileErrors.Sort(LintDataComparer);
        return fileErrors;
    }

    private int LintDataComparer(JsLintData x, JsLintData y)
    {
        return x.Line.CompareTo(y.Line);
    }

    public static bool ShouldIgnore(string file)
    {
        if (NotJsOrMinifiedOrDocumentOrNotExists(file))
        {
            return true;
        }

        string name = Path.GetFileName(file);
        return _builtInIgnoreRegex.IsMatch(name);
    }

    private static Regex _builtInIgnoreRegex = new Regex("(" + String.Join(")|(", new[] {
        @"_references\.js",
        @"amplify\.js",
        @"angular\.js",
        @"backbone\.js",
        @"bootstrap\.js",
        @"dojo\.js",
        @"ember\.js",
        @"ext-core\.js",
        @"handlebars.*",
        @"highlight\.js",
        @"history\.js",
        @"jquery-([0-9\.]+)\.js",
        @"jquery.blockui.*",
        @"jquery.validate.*",
        @"jquery.unobtrusive.*",
        @"jquery-ui-([0-9\.]+)\.js",
        @"json2\.js",
        @"knockout-([0-9\.]+)\.js",
        @"MicrosoftAjax([a-z]+)\.js",
        @"modernizr-([0-9\.]+)\.js",
        @"mustache.*",
        @"prototype\.js ",
        @"qunit-([0-9a-z\.]+)\.js",
        @"require\.js",
        @"respond\.js",
        @"sammy\.js",
        @"scriptaculous\.js ",
        @"swfobject\.js",
        @"underscore\.js",
        @"webfont\.js",
        @"yepnope\.js",
        @"zepto\.js",
        }) + ")", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}