// SPDX-License-Identifier: GPL-3.0-or-later
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
using System.Collections.Concurrent;
using System.Threading;

namespace SimRailConnect;

public enum TelemetryCommandKind
{
    PyscreenWrite,
    DriverControl,
    InvalidateTelemetry
}

public sealed class TelemetryCommand
{
    public string Id { get; init; } = "";
    public TelemetryCommandKind Kind { get; init; }
    public string Target { get; init; } = "";
    public string Action { get; init; } = "";
    public string? Field { get; init; }
    public int? Index { get; init; }
    public int Instance { get; init; }
    public double NumberValue { get; init; }
    public bool BoolValue { get; init; }
    public string Reason { get; init; } = "";
}

public static class TelemetryCommandQueue
{
    private const int MaxQueuedCommands = 128;
    private static readonly ConcurrentQueue<TelemetryCommand> Queue = new();
    private static int _count;

    public static bool TryEnqueue(TelemetryCommand command, out string error)
    {
        // CAS loop: atomically reserve a slot without O(n) Count snapshot
        int current;
        do
        {
            current = Volatile.Read(ref _count);
            if (current >= MaxQueuedCommands)
            {
                error = "Command queue is full";
                return false;
            }
        } while (Interlocked.CompareExchange(ref _count, current + 1, current) != current);

        try
        {
            Queue.Enqueue(command);
        }
        catch
        {
            Interlocked.Decrement(ref _count);
            throw;
        }
        error = "";
        return true;
    }

    public static bool TryDequeue(out TelemetryCommand? command)
    {
        if (Queue.TryDequeue(out command))
        {
            Interlocked.Decrement(ref _count);
            return true;
        }
        return false;
    }

    public static int Count => Volatile.Read(ref _count);
}
