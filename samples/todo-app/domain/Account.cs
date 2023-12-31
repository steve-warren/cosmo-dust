﻿namespace Cosmodust.Samples.TodoApp.Domain;

public interface IDomainEvent { }

public class Account
{
    private readonly HashSet<IDomainEvent> _domainEvents = new();

    public Account(string id)
    {
        Id = id;
    }

    public string Id { get; init; }
    public int NumberOfLists { get; private set; }

    public void AddList() =>
        NumberOfLists++;

    public void RemoveList() =>
        NumberOfLists--;
}
