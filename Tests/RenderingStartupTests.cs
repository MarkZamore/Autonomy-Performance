using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

// Development-only harness. Calls the production entry point in a fresh STA
// process; no generated action runner is invoked and no system settings change.
static class RenderingStartupTests
{
    const string SwitchName = "Switch.System.Windows.Media.MediaContext.DisableDirtyRectangles";
    static Exception failure;
    static int assertions;
    static readonly ManualResetEvent observed = new ManualResetEvent(false);
    static readonly ManualResetEvent completed = new ManualResetEvent(false);
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Interlocked.Increment(ref assertions);
    }

    // Inspect actual compiled startup order. A late SetSwitch can look correct in
    // AppContext while WPF still uses an earlier cached value, so both are tested.
    static void CheckFirstCall(MethodInfo entry)
    {
        byte[] code = entry.GetMethodBody().GetILAsByteArray();
        OpCode[] one = new OpCode[256], two = new OpCode[256];
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public))
        {
            OpCode op = (OpCode)field.GetValue(null);
            if (op.Size == 1) one[(byte)op.Value] = op;
            else two[(byte)op.Value] = op;
        }
        string name = null;
        bool enabled = false;
        for (int offset = 0; offset < code.Length;)
        {
            OpCode op = code[offset++] == 0xfe ? two[code[offset++]] : one[code[offset - 1]];
            int operand = offset;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: offset += 1; break;
                case OperandType.InlineVar: offset += 2; break;
                case OperandType.InlineI8:
                case OperandType.InlineR: offset += 8; break;
                case OperandType.InlineSwitch: offset += 4 + 4 * BitConverter.ToInt32(code, operand); break;
                default: offset += 4; break;
            }
            if (op == OpCodes.Ldstr) name = entry.Module.ResolveString(BitConverter.ToInt32(code, operand));
            if (op == OpCodes.Ldc_I4_1) enabled = true;
            if (op != OpCodes.Call && op != OpCodes.Callvirt && op != OpCodes.Newobj) continue;
            MethodBase first = entry.Module.ResolveMethod(BitConverter.ToInt32(code, operand));
            Assert(first.DeclaringType == typeof(AppContext) && first.Name == "SetSwitch",
                "Production startup calls another method before configuring dirty rectangles.");
            Assert(name == SwitchName && enabled, "Production startup does not enable the expected rendering switch.");
            return;
        }
        throw new InvalidOperationException("Production startup has no switch call.");
    }

    static void ObserveAfterSwitch()
    {
        try
        {
            Stopwatch timeout = Stopwatch.StartNew();
            bool enabled;
            // Do not touch any WPF type until the real entry point enables it.
            while (!AppContext.TryGetSwitch(SwitchName, out enabled) || !enabled)
            {
                if (timeout.ElapsedMilliseconds > 20000) throw new TimeoutException("Production entry point did not enable the rendering switch.");
                Thread.Sleep(10);
            }
            ObserveApplication();
        }
        catch (Exception error)
        {
            failure = error;
            observed.Set();
            Console.Error.WriteLine(error);
            Environment.Exit(1);
        }
    }

    static void ObserveApplication()
    {
        Stopwatch timeout = Stopwatch.StartNew();
        Application app;
        while ((app = Application.Current) == null)
        {
            if (timeout.ElapsedMilliseconds > 20000) throw new TimeoutException("Production Application was not created.");
            Thread.Sleep(10);
        }
        app.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate
        {
            try
            {
                Window window = app.MainWindow;
                Assert(window != null, "Production main window was not assigned.");
                // This callback was queued while ConstructorWindow was being
                // built. Send priority places it before the scheduled Show.
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -30000;
                window.Top = -30000;
                Assert(new WindowInteropHelper(window).EnsureHandle() != IntPtr.Zero,
                    "Production window did not create its native rendering surface.");
                bool enabled;
                Assert(AppContext.TryGetSwitch(SwitchName, out enabled) && enabled,
                    "Rendering switch is not enabled in the actual application.");
                Type switches = typeof(Visual).Assembly.GetType("MS.Internal.CoreAppContextSwitches", true);
                PropertyInfo cached = switches.GetProperty("DisableDirtyRectangles", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                Assert(cached != null, "Installed WPF does not expose the expected rendering switch.");
                Assert((bool)cached.GetValue(null, null), "WPF cached the rendering switch as disabled.");
                Assert(RenderOptions.ProcessRenderMode == RenderMode.Default,
                    "Production startup unexpectedly forced software rendering.");
                Assert(window.Content != null, "Production window content was not constructed.");
                window.ApplyTemplate();
                window.UpdateLayout();
                Assert(window.MinWidth == 820 && window.MinHeight == 560,
                    "Rendering change altered the production minimum window size.");
            }
            catch (Exception error) { failure = error; }
            finally
            {
                observed.Set();
                app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { app.Shutdown(); }));
            }
        }));
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Assert(args.Length == 1, "Pass the built production executable path.");
            Assembly production = Assembly.LoadFrom(args[0]);
            Assert(production.EntryPoint != null && production.EntryPoint.DeclaringType.Name == "ConstructorApp",
                "The inspected file is not the constructor executable.");
            CheckFirstCall(production.EntryPoint);
            AppContext.SetSwitch(SwitchName, false);
            Thread watchdog = new Thread(delegate()
            {
                if (!completed.WaitOne(45000))
                {
                    Console.Error.WriteLine("Timed out waiting for the production WPF startup probe.");
                    Environment.Exit(124);
                }
            });
            watchdog.IsBackground = true;
            watchdog.Start();
            Thread observer = new Thread(ObserveAfterSwitch);
            observer.IsBackground = true;
            observer.Start();
            object result = production.EntryPoint.Invoke(null, null);
            Assert(observed.WaitOne(0), "Production Main exited without observing application initialization.");
            if (failure != null) throw failure;
            Assert(result is int && (int)result == 0, "Production Main did not complete normally.");
            Console.WriteLine("PASS: " + assertions + " rendering startup checks; actual production Main, cached WPF switch, native window, hardware rendering allowed.");
            Console.WriteLine("This offscreen regression does not establish whether visible resizing artifacts are resolved.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { completed.Set(); }
    }
}
