namespace CodexTokenBar.Tests;

internal sealed record TestCase(string Name, Func<Task> Body);

internal static class TestRunner
{
    public static async Task<int> RunAsync(string[] filters, params IReadOnlyList<TestCase>[] groups)
    {
        var allTests = groups.SelectMany(group => group).ToArray();
        var selected = filters.Length == 0
            ? allTests
            : allTests.Where(test => filters.Any(filter =>
                test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToArray();

        if (selected.Length == 0)
        {
            Console.Error.WriteLine("No tests matched the supplied filters.");
            return 2;
        }

        var failures = 0;
        foreach (var test in selected)
        {
            try
            {
                await test.Body();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception}");
            }
        }

        Console.WriteLine($"RESULT {selected.Length - failures}/{selected.Length} passed");
        return failures == 0 ? 0 : 1;
    }
}

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected <{expected}> but got <{actual}>.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        var expectedArray = expected.ToArray();
        var actualArray = actual.ToArray();
        if (!expectedArray.SequenceEqual(actualArray))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expectedArray)}] but got [{string.Join(", ", actualArray)}].");
    }

    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown.");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown.");
    }
}
