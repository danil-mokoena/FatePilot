namespace FatePilot.Core;

// A fresh confirmation is required for each session, regardless of its entry point.
public sealed class StartConfirmation
{
    public bool Pending { get; private set; }
    public bool Request(bool running)
    {
        if (running) return false;
        Pending = true;
        return true;
    }
    public bool Confirm(bool running)
    {
        var accepted = Pending && !running;
        Pending = false;
        return accepted;
    }
    public void Cancel() => Pending = false;
}
