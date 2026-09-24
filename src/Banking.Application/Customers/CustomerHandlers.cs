using Banking.Application.Abstractions;
using Banking.Application.Common;
using Banking.Contracts.Events;
using Banking.Domain.Accounts;
using Banking.Domain.Common;

namespace Banking.Application.Customers;

public sealed record CustomerView(Guid Id, string Name, string Document, string Status, DateTimeOffset CreatedAt)
{
    public static CustomerView From(Customer customer, Cpf document) =>
        new(customer.Id, customer.Name, document.Masked, Codes.Of(customer.Status), customer.CreatedAt);
}

public sealed record RegisterCustomerCommand(Actor Actor, string? Name, string? Document);

public sealed class RegisterCustomerHandler(
    ICustomerRepository customers, IUnitOfWork unitOfWork, IOutbox outbox, IDocumentProtector protector, TimeProvider time)
{
    public async Task<Result<CustomerView>> HandleAsync(RegisterCustomerCommand command, CancellationToken cancellationToken)
    {
        if (!Cpf.TryParse(command.Document, out var cpf))
        {
            return Error.Validation("invalid_document", "CPF inválido.");
        }

        Customer customer;
        try
        {
            customer = Customer.Register(command.Actor.Subject, command.Name ?? string.Empty, cpf, protector, time.GetUtcNow());
        }
        catch (DomainException error)
        {
            return Error.Validation(error.Code, error.Message);
        }

        // Mesma resposta para identidade já cadastrada e CPF já usado: não revela se um CPF é cliente.
        if (await customers.FindBySubjectAsync(command.Actor.Subject, cancellationToken) is not null
            || await customers.ExistsWithDocumentAsync(customer.DocumentBlindIndex, cancellationToken))
        {
            return RegistrationConflict();
        }

        customers.Add(customer);
        outbox.Enqueue(new CustomerRegisteredV1(customer.Id), customer.CreatedAt);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return RegistrationConflict();
        }

        return CustomerView.From(customer, cpf);
    }

    private static Error RegistrationConflict() =>
        Error.Conflict("customer_registration_conflict", "Não foi possível cadastrar o cliente com esses dados.");
}

public sealed class GetCustomerHandler(ICustomerRepository customers, IDocumentProtector protector)
{
    public async Task<Result<CustomerView>> HandleAsync(Actor actor, Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await customers.FindAsync(customerId, cancellationToken);
        if (customer is null || (!actor.IsOperator && customer.Subject != actor.Subject))
        {
            return Error.NotFound("Cliente");
        }

        return CustomerView.From(customer, customer.RevealDocument(protector));
    }

    public async Task<Result<CustomerView>> HandleCurrentAsync(Actor actor, CancellationToken cancellationToken)
    {
        var customer = await customers.FindBySubjectAsync(actor.Subject, cancellationToken);
        return customer is null
            ? Error.NotFound("Cliente")
            : CustomerView.From(customer, customer.RevealDocument(protector));
    }
}
