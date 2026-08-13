while (true)
{
    Console.WriteLine(new string('o', 8_192));
    Console.Error.WriteLine(new string('e', 8_192));
    await Task.Delay(1);
}
