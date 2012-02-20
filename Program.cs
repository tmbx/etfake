using kcslib;
using kwmlib;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Threading;
using System.Diagnostics;
using System.IO;

namespace etfake
{
    /// <summary>
    /// Represent the EtFake program.
    /// </summary>
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main(String[] args)
        {
            // Initialize our application.
            AppInit();

            // We can catch fatal errors here without risking an infinite KWM
            // spawn loop.
            WmUi.FatalErrorMsgOKFlag = true;

            // Execute the bootstrap method when the message loop is running.
            KBase.ExecInUI(new KBase.EmptyDelegate(Bootstrap));

            try
            {
                // Run the message loop.
                Application.Run();
            }

            catch (Exception ex)
            {
                KBase.HandleException(ex, true);
            }
        }

        /// <summary>
        /// Request the application to quit as soon as possible.
        /// </summary>
        public static void RequestAppExit()
        {
            Application.Exit();
        }

        /// <summary>
        /// Initialize the application on startup.
        /// </summary>
        private static void AppInit()
        {
            // Somehow this call doesn't make the output visible in cygwin 
            // bash. It works for cmd.exe.
            KSyscalls.AttachConsole(KSyscalls.ATTACH_PARENT_PROCESS);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            KBase.InvokeUiControl = new Control();
            KBase.InvokeUiControl.CreateControl();
            KBase.HandleErrorCallback = WmUi.HandleError;
            Application.ThreadException += HandleUnhandledException;
            KwmCfg.Cur = KwmCfg.Spawn();
            KLogging.Logger = KwmLogger.Logger;
            KwmLogger.SetLoggingLevel(KwmCfg.Cur.KwmDebuggingFlag ? KwmLoggingLevel.Normal : KwmLoggingLevel.Debug);
        }

        /// <summary>
        /// Handle an unhandled exception escaping from a thread, including the
        /// main thread. Supposedly. That's what they said about
        /// AppDomain.CurrentDomain.UnhandledException and it just did so in
        /// debug mode.
        /// </summary>
        private static void HandleUnhandledException(Object sender, ThreadExceptionEventArgs args)
        {
            KBase.HandleException(args.Exception, true);
        }

        /// <summary>
        /// This method is executed when the application has started and the
        /// message loop is running.
        /// </summary>
        private static void Bootstrap()
        {
            // Setup the client broker.
            Client.Setup(true);

            // Setup the console.
            ConsoleWindow console = new ConsoleWindow();
            console.Show();
            console.OnConsoleClosing += delegate(Object sender, EventArgs args)
            {
                Client.Broker.TryStop();
                RequestAppExit();
            };
        }

        /// <summary>
        /// Reference to the client.
        /// </summary>
        private static EAnpTester Client = new EAnpTester();
    }

    /// <summary>
    /// Class used to test EAnp interactions.
    /// </summary>
    public class EAnpTester
    {
        public bool ClientFlag = false;
        public EAnpBaseBroker Broker;
        Queue<AnpMsg> CmdQueue = new Queue<AnpMsg>();

        public void Setup(bool clientFlag)
        {
            ClientFlag = clientFlag;
            if (ClientFlag) Broker = new EAnpClientBroker();
            else Broker = new EAnpServerBroker();
            Broker.OnClose += HandleBrokerClosed;
            Broker.OnChannelOpen += HandleChannelOpen;
            Broker.Start();

            if (clientFlag)
            {
                String wslPath = "C:/test.wsl";
                AnpMsg m = null;

                // Register, create a workspace, export it.
#if false
                m = QueueCmd(EAnpCmd.RegisterKps);
                m.AddUInt32(1);
                m.AddString("deploy");
                m.AddString("test1@teambox.co");
                m.AddString("test1");

                m = QueueCmd(EAnpCmd.CreateKws);
                m.AddString("test EtFake");
                m.AddUInt32(0);

                m = QueueCmd(EAnpCmd.ExportKws);
                m.AddUInt64(0);
                m.AddString(wslPath);

#else
                // Import a workspace.
                m = QueueCmd(EAnpCmd.ImportKws);
                m.AddString(File.ReadAllText(wslPath));
#endif

                // Miscellaneous commands.

                // Notice that this one is slow enough to make the workspace
                // come online, on join.
#if false
                m = QueueCmd(EAnpCmd.LookupRecAddr);
                m.AddUInt32(1);
                m.AddString("karim.yaghmour@teambox.co");
#endif

#if false
                m = QueueCmd(EAnpCmd.SetLoginPwd);
                m.AddUInt64(1);
                m.AddString("foobara");
#endif

#if false
                m = QueueCmd(EAnpCmd.InviteKws);
                m.AddUInt64(1);
                m.AddUInt32(1);
                m.AddString("hello");
                m.AddUInt32(1);
                m.AddString("Test 2");
                m.AddString("test2@teambox.co");
                m.AddUInt64(0);
                m.AddString("");
                m.AddString("");
#endif

#if false
                m = QueueCmd(EAnpCmd.ChatPostMsg);
                m.AddUInt64(1);
                m.AddUInt32(0);
                m.AddString("Hey there");
#endif

#if false
                m = QueueCmd(EAnpCmd.VncCreateSession);
                m.AddUInt64(1);
                m.AddUInt32(1);
                m.AddString("Foobar");
#endif

#if false
                m = QueueCmd(EAnpCmd.SetKwsTask);
                m.AddUInt64(1);
                m.AddUInt32((uint)KwsTask.DeleteRemotely);
#endif
            }
        }

        public void Tell(String msg)
        {
            String who = Broker is EAnpClientBroker ? "Client" : "Server";
            KLogging.Log(who + ": " + msg);
        }

        public AnpMsg QueueCmd(EAnpCmd type)
        {
            AnpMsg cmd = new AnpMsg();
            cmd.Type = (uint)type;
            CmdQueue.Enqueue(cmd);
            return cmd;
        }

        public void ExecNextCmd(EAnpChannel c)
        {
            if (CmdQueue.Count == 0)
            {
                Tell("All commands executed");
                return;
            }

            AnpMsg cmd = CmdQueue.Dequeue();
            Tell("Running command " + (EAnpCmd)cmd.Type);
            EAnpOutgoingQuery q = c.SendCmd(cmd);
            if (q == null) return;
            q.OnCompletion += HandleOutcomingCompletion;
        }

        public void HandleBrokerClosed(Object sender, EventArgs args)
        {
            Tell("Broker stopped");
            Debug.Assert(Broker.TryStop());
        }

        public void HandleChannelOpen(Object sender, EAnpChannelOpenEventArgs args)
        {
            Tell("Channel opened");
            EAnpChannel c = args.Channel;
            if (!c.IsOpen()) return;
            c.OnIncomingQuery += HandleIncomingQuery;
            c.OnIncomingEvent += HandleIncomingEvent;
            c.OnClose += HandleChannelClosed;
            ExecNextCmd(c);
        }

        public void HandleChannelClosed(Object sender, EventArgs args)
        {
            EAnpChannel c = (EAnpChannel)sender;
            String reason = c.Ex == null ? "no error" : c.Ex.Message;
            Tell("Channel closed: " + reason);
        }

        public void HandleIncomingQuery(Object sender, EAnpIncomingQueryEventArgs args)
        {
            Tell("Incoming query received");
            EAnpIncomingQuery q = args.Query;
            if (!q.IsPending()) return;
            q.OnCancellation += new EventHandler(OnQueryCancellation);

            AnpMsg m = new AnpMsg();
            m.Type = (uint)EAnpRes.OK;
            q.Reply(m);
        }

        public void HandleIncomingEvent(Object sender, EAnpIncomingEventEventArgs args)
        {
            Tell("Incoming event received");
        }

        public void OnQueryCancellation(object sender, EventArgs e)
        {
            Tell("Incoming query cancelled");
        }

        public void HandleOutcomingCompletion(Object sender, EventArgs args)
        {
            EAnpOutgoingQuery q = (EAnpOutgoingQuery)sender;

            try
            {
                if (q.Ex != null) Tell("Command failed: " + q.Ex.Message);
                else
                {
                    EAnpRes r = (EAnpRes)q.Res.Type;
                    Tell("Command completed: result type is " + r);

                    if (r == EAnpRes.Failure)
                    {
                        EAnpFailType f = (EAnpFailType)q.Res.Elements[0].UInt32;
                        String msg = q.Res.Elements[1].String;
                        Tell("Failure type " + f + ", message " + msg);
                    }

                    else
                    {
                        ExecNextCmd(q.Channel);
                    }
                }
            }

            catch (Exception ex)
            {
                Tell(ex.Message);
            }
        }
    }
}