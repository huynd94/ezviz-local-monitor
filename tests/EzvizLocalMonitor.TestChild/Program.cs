using EzvizLocalMonitor.Headless.Hosting;
using EzvizLocalMonitor.Services;

if (args.Length != 2 || args[0] != "hold-lock") return 64;
using var held = StateDirectoryLock.Acquire(new AppPaths(args[1], "/tmp/model.onnx"));
Console.WriteLine("LOCKED");
Console.Out.Flush();
Console.ReadLine();
return 0;
