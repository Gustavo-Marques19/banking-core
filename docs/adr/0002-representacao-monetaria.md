# ADR-002: Representação monetária

- Status: aceita
- Data: 2026-09-24

## Contexto

A spec propõe `Money(decimal Amount, string Currency)` como `readonly record struct`. Três problemas:

- `default(Money)` é um valor válido para o compilador (`0`, `null`) e passa por fora de qualquer validação do construtor.
- `string` aceita `"brl"`, `"XYZ"` ou vazio.
- No JSON, `1000.00` como número vira `float64` em clientes JavaScript. A escala também pode chegar errada (`100.001`) e ser arredondada em silêncio em algum ponto.

## Decisão

**Domínio**

- `Currency` é um value object com o código ISO 4217 alfa-3 e o expoente da menor unidade (BRL = 2). Na Fase 1 existe uma lista fechada com só `BRL`. Qualquer outro código é erro de validação.
- `Money` é um `sealed record` (classe, não struct), com construtor privado e duas fábricas:
  - `Money.FromMinor(long minor, Currency currency)`;
  - `Money.Parse(string amount, Currency currency)`.
- Aritmética com `checked`. Operação entre moedas diferentes lança `CurrencyMismatchException`.
- `Money` pode ser negativo, porque contas de sistema podem ter saldo negativo. A exigência de valor positivo é regra do lançamento e da operação, não do `Money`.

**Banco**

- Valores em `bigint` de centavos (`amount_minor`), com `currency char(3)` e `CHECK (currency ~ '^[A-Z]{3}$')`.

**API**

- Entrada e saída com valor como string decimal e a moeda explícita:

  ```json
  { "amount": "100.00", "currency": "BRL" }
  ```

- Regras do parse para BRL:
  - formato `^(0|[1-9][0-9]{0,14})(\.[0-9]{1,2})?$`;
  - mais casas decimais que o expoente é **rejeitado**, nunca arredondado (`"100.001"` devolve 400);
  - sinal, notação científica, vírgula e espaços são rejeitados;
  - número JSON no lugar de string é rejeitado.
- A saída sempre usa o número de casas do expoente (`"100.00"`, nunca `"100"`).
- O teto por operação não é regra do `Money`: fica nos limites de negócio (M4).

## Alternativas consideradas

- **`decimal` em tudo, `numeric(19,4)` no banco.** Funciona, porque `decimal` é exato no .NET. Perde para `bigint` porque a escala continua sendo uma decisão em cada fronteira (EF, JSON, SQL), e cada fronteira é uma chance de arredondar. Com centavos inteiros, não há escala para errar.
- **`readonly record struct` com validação.** Não resolve `default(Money)`. Validar em todo método que recebe `Money` é pior que tornar o estado inválido impossível de construir.

## Consequências

- Somas no banco são inteiras e exatas. O limite de `long` (9,2 × 10¹⁸ centavos) está muito acima do teto de 15 dígitos inteiros.
- Clientes da API precisam enviar string. Isso vai documentado no OpenAPI com exemplo.
- Suportar outra moeda exige incluí-la na lista e revisar os testes de parse, sem mudar o schema.
- Como `Money` é classe, `default` vira `null` e o compilador acusa com nullable reference types habilitado (`<Nullable>enable</Nullable>` e warnings como erro).
- Testes obrigatórios: não existe `Money` sem moeda válida, moedas diferentes lançam exceção, escala excedente é rejeitada e overflow lança exceção.
