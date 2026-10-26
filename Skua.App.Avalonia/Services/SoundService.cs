using Skua.Core.Interfaces;
using System;

namespace Skua.App.Avalonia.Services;

public class SoundService : ISoundService
{
    public void Beep()
    {
        try
        {
            Console.Beep();
        }
        catch
        {
        }
    }

    public void Beep(int frequency, int duration)
    {
        try
        {
            Console.Beep();
        }
        catch
        {
        }
    }
}
