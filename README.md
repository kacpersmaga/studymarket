# StudyMarket API

Backend marketplace'u kursów edukacyjnych: ASP.NET Core 10, PostgreSQL, płatności Stripe.

## Wymagania

- .NET 10 SDK
- Docker (baza PostgreSQL)

## Uruchomienie

```bash
docker compose up -d
dotnet run --project src/StudyMarket.Api
```

Baza działa na porcie 5433, API na `http://localhost:5254`. Schemat bazy tworzy się przy starcie (migracje EF Core).

- dokumentacja interaktywna: http://localhost:5254/scalar
- dokument OpenAPI: http://localhost:5254/openapi/v1.json
- stan API i bazy: http://localhost:5254/health (200 albo 503)

Przy pierwszym starcie na pustej bazie (środowisko Development) ładują się dane demonstracyjne. Hasło każdego konta to `Demo123!`:

- `admin@studymarket.dev` – administrator, widzi zgłoszenia i może edytować każdy kurs;
- `tworca@studymarket.dev` – twórca z dwoma opublikowanymi kursami, jednym szkicem i sprzedażą;
- `uczen@studymarket.dev` – uczeń, który kupił kurs chemii i wystawił opinię.

Żeby zacząć od zera, usuń wolumen bazy: `docker compose down -v`.

Testy uruchamia `dotnet test` (wymaga działającego Dockera, bo testy integracyjne startują własny kontener PostgreSQL).

## Autoryzacja

`POST /api/auth/login` i `POST /api/auth/register` zwracają `{ "token": "..." }`. Token wysyła się w nagłówku:

```
Authorization: Bearer <token>
```

Token jest ważny 12 godzin i nie ma odświeżania – po wygaśnięciu trzeba zalogować się ponownie. Rejestracja wymaga pól `email`, `password` (min. 8 znaków), `displayName`, `role` (`Student` albo `Creator`), `birthDate` i `acceptTerms: true`.

## CORS

API przyjmuje żądania z przeglądarki tylko z adresów wpisanych w `Cors:Origins` w `src/StudyMarket.Api/appsettings.json`. Domyślnie jest tam `http://localhost:3000`. Jeśli frontend działa na innym porcie, dopisz go do tej listy.

## Zakup kursu

1. `POST /api/orders` z `{ "courseId": 1, "consentToImmediateAccess": true }` – tworzy zamówienie w stanie `Pending`.
2. `POST /api/orders/{id}/checkout` – zwraca `{ "paymentUrl": "..." }`.
3. Przekierowanie przeglądarki na `paymentUrl` (strona płatności Stripe).
4. Stripe wraca na `Stripe:SuccessUrl` (domyślnie `http://localhost:3000/payment/success`) albo `Stripe:CancelUrl` (`/payment/cancel`).
5. Na stronie powrotu odpytuj `GET /api/orders`, aż zamówienie ma status `Paid`. Płatność potwierdza webhook Stripe, który może dojść kilka sekund po powrocie, więc sam powrót niczego nie oznacza.

### Stripe lokalnie

Bez kluczy działa wszystko poza przekierowaniem do płatności. Żeby przetestować pełny zakup:

1. Załóż konto Stripe i w trybie testowym włącz metody BLIK i Przelewy24.
2. Wpisz klucz `sk_test_...` do `Stripe:SecretKey` w `appsettings.json`.
3. Uruchom [Stripe CLI](https://docs.stripe.com/stripe-cli) i wpisz wypisany sekret webhooka do `Stripe:WebhookSecret`:

```bash
stripe listen --forward-to localhost:5254/api/payments/stripe-webhook
```

## Strona kursu

`GET /api/courses/{id}` zwraca `canEdit` i `hasAccess`. Gdy `canEdit` jest `true`, pokaż „Edytuj”; gdy `hasAccess` – „Ucz się”; gdy oba są `false` – „Kup”. Szkic kursu widzi tylko jego autor (i administrator).

## Obrazki w notatkach

Notatka lekcji (`content`) jest w Markdownie. Twórca wgrywa plik przez `POST /api/lessons/{id}/attachments` (multipart, pole `file`; PDF, PNG lub JPG do 20 MB) i wstawia go do notatki jako:

```
![opis](attachment:ID)
```

Przy wyświetlaniu lekcji (`GET /api/lessons/{id}`) podmień `attachment:ID` na `url` z listy `attachments` tej lekcji. Ten link jest podpisany, działa w `<img src>` bez tokenu i wygasa po godzinie, dlatego nie zapisuj go w treści notatki. Markdown renderuj bezpiecznie (ochrona przed XSS).

## Błędy

Błędy mają format ProblemDetails (RFC 9457). Komunikat dla użytkownika jest w polu `detail`, a błędy walidacji formularza w `errors` (klucz to nazwa pola).

`429 Too Many Requests` oznacza przekroczony limit żądań z jednego adresu IP: 300 na minutę dla całego API i 10 na minutę dla logowania, rejestracji i zmiany hasła. Wystarczy komunikat w stylu „spróbuj za minutę”.

Pełna lista endpointów i modeli jest w dokumentacji `/scalar`.

## Pokrycie kodu i wydajność

Raport pokrycia kodu (HTML w katalogu `coverage/`):

```bash
dotnet test -- --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
```
```bash
dotnet tool restore
```
```bash
dotnet tool run reportgenerator -reports:tests/StudyMarket.Tests/bin/Debug/net10.0/TestResults/coverage.cobertura.xml -targetdir:coverage -reporttypes:Html -assemblyfilters:+StudyMarket.Api -classfilters:"-StudyMarket.Api.Data.Migrations.*;-Microsoft.*"
```

Test obciążeniowy: `tools/LoadTest.cs` wysyła żądania GET z N równoległych klientów przez zadaną liczbę sekund i wypisuje liczbę żądań na sekundę oraz czasy odpowiedzi p50/p95/p99. Najpierw uruchom API w trybie Release na osobnym porcie, z podniesionym limitem żądań (inaczej większość odpowiedzi to 429):

```bash
RateLimit__RequestsPerMinute=1000000 dotnet run --project src/StudyMarket.Api -c Release -- --urls http://localhost:5260
```

Potem w drugim terminalu (adres, liczba klientów, czas w sekundach, opcjonalnie token JWT):

```bash
dotnet run tools/LoadTest.cs -- http://localhost:5260/api/courses 50 20
```
