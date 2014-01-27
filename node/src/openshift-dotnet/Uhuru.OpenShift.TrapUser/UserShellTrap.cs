﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Uhuru.Openshift.Runtime;
using Uhuru.Openshift.Runtime.Config;
using Uhuru.Openshift.Runtime.Utils;


namespace Uhuru.OpenShift.TrapUser
{
    public class UserShellTrap
    {
        private static void LoadEnv(string directory, StringDictionary targetList)
        {
            if (targetList == null)
            {
                throw new ArgumentNullException("targetList");
            }

            if (!Directory.Exists(directory))
            {
                return;
            }

            string[] envFiles = Directory.GetFiles(directory);

            foreach (string envFile in envFiles)
            {
                string varValue = File.ReadAllText(envFile);
                string varKey = Path.GetFileName(envFile);
                targetList[varKey] = varValue;
            }
        }

        private static void SetupGearEnv(StringDictionary targetList)
        {
            if (targetList == null)
            {
                throw new ArgumentNullException("targetList");
            }
  
            string globalEnv = Path.Combine(NodeConfig.ConfigDir, "env");

            string homeDir = Environment.GetEnvironmentVariable("HOME");

            UserShellTrap.LoadEnv(globalEnv, targetList);

            UserShellTrap.LoadEnv(Path.Combine(homeDir, ".env"), targetList);

            foreach (string dir in Directory.GetDirectories(Path.Combine(homeDir, ".env"), "*"))
            {
                LoadEnv(dir, targetList);
            }

            string[] userHomeDirs = Directory.GetDirectories(homeDir, "*", SearchOption.TopDirectoryOnly);

            foreach (string userHomeDir in userHomeDirs)
            {
                LoadEnv(Path.Combine(userHomeDir, "env"), targetList);
            }
        }

        public static int StartShell(string args)
        {
            string assemblyLocation = Path.GetDirectoryName(typeof(UserShellTrap).Assembly.Location);
            string rcfile = Path.Combine(assemblyLocation, @"mcollective\cmdlets\powershell-alias.sh");

            ProcessStartInfo shellStartInfo = new ProcessStartInfo();
            shellStartInfo.EnvironmentVariables["CYGWIN"] = "nodosfilewarning winsymlinks:native";
            // shellStartInfo.FileName = "bash";

            // SetupGearEnv(shellStartInfo.EnvironmentVariables);
            string gearUuid = shellStartInfo.EnvironmentVariables.ContainsKey("OPENSHIFT_GEAR_UUID") ? shellStartInfo.EnvironmentVariables["OPENSHIFT_GEAR_UUID"] : string.Empty;

            var userToTrap = gearUuid; // TODO: get the prison's username from the process arguments or environment variables
           
            var prisons = Prison.Prison.Load();

            //var prison = prisons.First(p => p.Tag == userToTrap);
            #region test
            Uhuru.Prison.Prison prison = new Uhuru.Prison.Prison();
            prison.Tag = "oo";
            Uhuru.Prison.PrisonRules prisonRules = new Uhuru.Prison.PrisonRules();
            prisonRules.CellType = Uhuru.Prison.RuleType.None;
            prisonRules.CellType = Uhuru.Prison.RuleType.WindowStation;

            prison.Lockdown(prisonRules);
            #endregion

            string arguments = string.Empty;
            if (args.StartsWith("\""))
            {
                arguments = Regex.Replace(args, @"\A""[^""]+""\s", "");
            }
            else
            {
                arguments = Regex.Replace(args, @"\A[^\s]+", "");
            }

            // shellStartInfo.Arguments = string.Format(@"--rcfile ""{0}"" {1}", rcfile, arguments);
            // shellStartInfo.UseShellExecute = false;



            Logger.Debug("Starting trapped bash for gear {0}", gearUuid);

            //Process shell = Process.Start(shellStartInfo);
            //shell.WaitForExit();

            var process = prison.Execute(@"C:\cygwin\installation\bin\bash.exe", string.Format(@"--rcfile ""{0}"" {1}", rcfile, arguments));

            process.WaitForExit();

            // Every time the user's session ends, set ownership of files and fix symlinks in the app deployments dir.
            if (shellStartInfo.EnvironmentVariables.ContainsKey("OPENSHIFT_HOMEDIR") && Directory.Exists(shellStartInfo.EnvironmentVariables["OPENSHIFT_HOMEDIR"]))
            {
                Logger.Debug("Setting ownership and acls for gear {0}", gearUuid);
                try
                {
                    LinuxFiles.TakeOwnership(shellStartInfo.EnvironmentVariables["OPENSHIFT_HOMEDIR"], Environment.UserName);
                }
                catch (Exception ex)
                {
                    Logger.Error("There was an error while trying to take ownership for files in gear {0}: {1} - {2}", gearUuid, ex.Message, ex.StackTrace);
                }

                Logger.Debug("Fixing symlinks for gear {0}", gearUuid);
                try
                {
                    LinuxFiles.FixSymlinks(shellStartInfo.EnvironmentVariables["OPENSHIFT_HOMEDIR"]);
                }
                catch (Exception ex)
                {
                    Logger.Error("There was an error while trying to fix symlinks for gear {0}: {1} - {2}", gearUuid, ex.Message, ex.StackTrace);
                }
            }
            else
            {
                Logger.Warning("Not fixing symlinks for gear {0}. Could not locate its home directory.", gearUuid);
            }

            //return shell.ExitCode;
            return process.ExitCode;
        }
    }
}
