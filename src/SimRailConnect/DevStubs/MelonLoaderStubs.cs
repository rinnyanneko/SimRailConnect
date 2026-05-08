/*
    SimRailConnect
    Copyright © 2026 rinnyanneko

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;

namespace MelonLoader;

/// <summary>
/// Compile-only stand-in used when a developer machine does not have
/// SimRail/MelonLoader installed. Real plugin builds must reference
/// MelonLoader.dll and will not compile this file.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class MelonInfoAttribute : Attribute
{
    public MelonInfoAttribute(Type melonType, string name, string version, string author)
    {
        MelonType = melonType;
        Name = name;
        Version = version;
        Author = author;
    }

    public Type MelonType { get; }
    public string Name { get; }
    public string Version { get; }
    public string Author { get; }
}

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class MelonGameAttribute : Attribute
{
    public MelonGameAttribute()
    {
    }
}

public abstract class MelonPlugin
{
    public MelonLogger.Instance LoggerInstance { get; } = new();

    public virtual void OnInitializeMelon()
    {
    }

    public virtual void OnDeinitializeMelon()
    {
    }
}

public static class MelonLogger
{
    public sealed class Instance
    {
        public void Msg(string message) => Console.WriteLine(message);

        public void Warning(string message) => Console.WriteLine("WARNING: " + message);

        public void Error(string message) => Console.Error.WriteLine("ERROR: " + message);
    }
}

public static class MelonPreferences
{
    public static MelonPreferences_Category CreateCategory(string identifier) => new(identifier);
}

public sealed class MelonPreferences_Category
{
    public MelonPreferences_Category(string identifier)
    {
        Identifier = identifier;
    }

    public string Identifier { get; }

    public MelonPreferences_Entry<T> CreateEntry<T>(string identifier, T defaultValue, string description)
    {
        return new MelonPreferences_Entry<T>(identifier, defaultValue, description);
    }
}

public sealed class MelonPreferences_Entry<T>
{
    public MelonPreferences_Entry(string identifier, T defaultValue, string description)
    {
        Identifier = identifier;
        Value = defaultValue;
        Description = description;
    }

    public string Identifier { get; }
    public T Value { get; set; }
    public string Description { get; }
}
