using ConsoleGame.Entities;
using ConsoleGame.Renderer;
using ConsoleRayTracing;

public static class Program
{
    public static Terminal terminal;

    private static void Main(string[] args)
    {
        Console.CursorVisible = false;

        terminal = new Terminal();

        int superSample = 1;
        if (args != null && args.Length > 0)
        {
            int parsed;
            if (int.TryParse(args[0], out parsed) && parsed > 0) superSample = parsed;
        }

        BaseEntity rt = new BaseEntity(0, 0, new Chexel());
        RaytraceEntity rtController = new RaytraceEntity(terminal, rt, superSample);

        rt.AddComponent(rtController);
        terminal.AddEntity(rt);

        terminal.Start();

        Console.ResetColor();
        Console.CursorVisible = true;
    }
}