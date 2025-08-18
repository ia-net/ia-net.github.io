---
Title: EF Core와 Dapper 데이터 접근 전략
Published: 2025-08-17
Layout: /_layouts/post.cshtml
Excerpt: ORM 추상화의 안정성과 경량 매퍼의 제어, 성능 분석
Tags: [ASP.NET, Data Access, EF Core, Dapper, Architecture]
---

## TL;DR
- 제가 이해하기로 **EF Core**는 모델 중심 개발과 스키마 이력 관리에 강하고, **Dapper**는 쿼리 제어와 조회 성능이 돋보입니다.  
- 한 도구로 통일하기보다는 **일반 CRUD는 EF Core**, **핫 패스 조회는 Dapper**로 나누면 실무 감각과도 잘 맞는 것 같습니다.  
- EF Core에서 제가 효과를 본 것: `.AsNoTracking()`, **프로젝션(Select)**, `AsSplitQuery()`, **CompiledQuery**, 적절한 인덱스.  
- Dapper에서 메모한 포인트: **SQL 일관성 규칙**, `QueryMultiple`/멀티매핑, **TVP(Table-valued Parameter)**, `buffered:false` 스트리밍, 타입 핸들러.  
- 마이그레이션은 EF Core 기본 기능을 쓰고, Dapper 위주라면 **FluentMigrator/DbUp** 같은 도구가 빈자리를 채워줍니다.  
- 테스트는 EF Core → **InMemory/SQLite + 컨테이너**, Dapper → **컨테이너 DB + 스냅샷 픽스처** 조합이 안정적이었습니다.
  
---

EF Core는 전통적인 ORM입니다. 도메인 모델과 관계를 코드로 표현하고, LINQ로 의도를 쓰면 내부에서 **스르륵** SQL을 만들어 실행합니다. 체인지 트래킹, 로딩 전략(Lazy/Eager/Explicit), 마이그레이션 같은 기능은 길게 가져가는 프로젝트에서 든든한 장비처럼 느껴졌습니다. 특히 스키마 변경을 마이그레이션으로 추적하고 배포 파이프라인에 자연스럽게 녹일 수 있어서, 팀 규모가 커질수록 체감 이득이 커지겠다는 생각이 들었습니다.

물론 추상화에는 늘 영수증이 따라옵니다. 복잡한 LINQ가 의도치 않은 조인 폭증이나 N+1을 몰래 끼워 넣을 수 있더군요. 

그래서 제가 적어둔 대응 방법은 이렇습니다:
- 읽기 경로에는 `.AsNoTracking()`을 적용해 가볍게 만들고, 프로젝션으로 필요한 컬럼만 고르며, 다중 Include가 필요할 땐 `AsSplitQuery()`로 쿼리 폭발을 막기. 
- 고정 빈도 쿼리는 CompiledQuery로 감싸 캐시 비용을 줄이고, EF Core의 `ExecuteUpdate/ExecuteDelete`로 라운드트립을 아끼기. 
- 원시 SQL이 필요하면 `FromSqlInterpolated`과 `FromSqlRaw`의 차이를 의식해서 선택하기. 

이 정도 기본기를 챙기니 CRUD는 빠름과 안정 사이에서 균형이 괜찮았습니다.

Dapper는 경량 매퍼입니다. 개발자가 작성한 SQL이 그대로 실행되고, 결과만 빠르게 객체로 매핑됩니다. 번역 단계가 없으니 성능은 ADO.NET에 가깝고, SQL 힌트, CTE, 윈도 함수 등 RDBMS의 툴들을 그대로 활용할 수 있어서 대량 조회·리포팅·대시보드처럼 읽기 중심 워크로드에서 피부로 느껴지는 이득이 있었습니다.

대신 책임도 함께 옵니다. 스키마 변화에 따른 SQL 유지보수는 개발자의 몫이라서, 팀 차원의 규칙과 도구가 꼭 필요해 보였습니다. 제가 메모한 운영 습관은 다음과 같습니다. 파라미터는 익명 객체/`DynamicParameters`로 일관되게 바인딩(보안·플랜 캐시에 유리), 여러 결과셋은 `QueryMultiple`로 왕복 횟수 절약, 대량 입력은 SQL Server 기준 TVP로 배치 처리, 큰 결과는 `buffered:false`로 스트리밍, 페이징은 커버링 인덱스 + `OFFSET/FETCH`로 계획 안정화. 스칼라/값 객체 변환은 TypeHandler로 표준화하면 코드가 단정해졌습니다.

**트랜잭션 처리**는 두 도구 모두 명확했습니다. EF Core는 `DbContext.Database.BeginTransaction()`과 실행 전략으로 원자성 + 내결함성을 챙길 수 있었고, 복잡한 비즈니스 흐름에서는 `Database.CreateExecutionStrategy()`로 재시도를 감싼 뒤 트랜잭션을 여는 패턴이 안정적이었습니다. Dapper는 `IDbTransaction`으로 범위를 명시해 커밋/롤백을 관리하는 방식이 직관적이었고요. 표현은 다르지만, 결국 중요한 건 “업무 규칙이 코드에서 잘 읽히는가?”라는 점이었습니다.

조인 전략도 관점의 차이였습니다. EF Core의 `Include`는 친절하지만, 과하면 중복 행이 늘어 부담이 커졌습니다. 성능이 중요한 경로에서는 `Select`로 DTO를 직접 투사해 정확히 필요한 조인만 표현하는 편이 안정적이었고, Dapper는 처음부터 끝까지 명시적이라서 뷰나 CTE로 읽기 모델 최적화를 해두고 쿼리를 짧게 유지하면 운영이 편했습니다.

**운영과 배포** 관점에서 EF Core는 마이그레이션이 표준처럼 느껴졌습니다. CI에서 마이그레이션 해시를 검증하고, CD 단계에서 `dotnet ef database update`를 실행하면 이력이 투명합니다(앱 기동 시 자동 적용은 다중 인스턴스 경합 위험으로 비추천). Dapper는 기본 제공이 없으니 FluentMigrator/DbUp/Flyway같은 도구로 파이프라인을 깔아두면 깔끔했습니다. 관측성도 접근법이 달랐는데, EF Core는 `LogTo`, `EnableSensitiveDataLogging`으로 번역 SQL과 파라미터를 손쉽게 로깅하고 EventCounters로 지표를 노출할 수 있었습니다. 근데 Dapper는 따로 내장 로거가 없어서 `IDbConnection`/`DbCommand` 래퍼로 실행 시간·행 수·오류를 표준 포맷으로 남겨두는 방식이 현실적이었습니다.

**테스트 전략**은 현실적으로 다음 조합이 편했습니다. EF Core는 SQLite(in-memory)로 빠른 단위 테스트를 돌리고, 실제 RDBMS 컨테이너(Testcontainers)로 통합 테스트를 함께 돌려 번역 차이에서 오는 의외의 실패를 조기에 잡기. Dapper는 번역 계층이 없으니 처음부터 컨테이너 DB로 가고, 여기에 쿼리 스냅샷 테스트와 픽스처 데이터를 더해 회귀를 막기. 별거아닌 작은 습관이지만 안심하고 리팩토링하기에 도움이 된 것 같네요.

**보안과 안정성**은 두 도구 모두 파라미터화로 기본기가 탄탄했습니다. 연결 풀은 ADO.NET 공통 레이어에서 관리되므로 `using`으로 연결을 열고 닫아도 풀에 반환됩니다. 다만 트래픽이 급증해 풀 고갈 신호가 보이면, 풀 크기를 올리기 전에 쿼리 최적화부터 점검하는 편이 훨씬 효과적이었습니다. 클라우드에서는 재시도 정책(예: Polly)을 EF Core 실행 전략 또는 Dapper 호출부에 한 번에 적용해 일시적 오류에 부드럽게 대응하는 구성이 마음이 놓였습니다.

아키텍처를 한 줄로 정리하면 지금의 저는 "쓰기는 EF Core, 읽기는 Dapper" 를 출발점으로 잡습니다. CQRS를 택했다면 Command 핸들러는 EF Core로 비즈니스 규칙과 트랜잭션을 표현하고, Query 핸들러는 Dapper로 화면/리포트에 맞춘 읽기 모델을 구성하는 그림이 자연스럽더군요. 마이크로서비스라면 도메인 성격에 따라 선택을 달리하는 것도 말이 됩니다. 무결성이 중요한 코어 도메인은 EF Core, 읽기 최적화가 핵심인 보조 도메인은 Dapper처럼요. 결국 핵심은 도구가 아니라, 원칙을 코드와 파이프라인에 녹여 일관되게 반복하는 습관이라고 느꼈습니다. 루틴이 실력을 만든다고 믿습니다.

```csharp
// EF Core - 읽기 경로 기본기
var items = await db.Orders
    .AsNoTracking()
    .Where(o => o.Status == Status.Open)
    .Select(o => new OrderDto {
        Id = o.Id,
        Total = o.Total,
        CustomerName = o.Customer.Name
    })
    .AsSplitQuery()
    .ToListAsync(ct);

// EF Core - 재시도 전략 안에서 트랜잭션 (SQL Server)
var strategy = db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // ... 작업들
    await tx.CommitAsync(ct);
});

// Dapper - 페이징 + 총계 동시 조회
using var conn = new SqlConnection(cs);
var sql = @"
WITH Q AS (
  SELECT o.Id, o.Total, c.Name AS CustomerName
  FROM Orders o
  JOIN Customers c ON c.Id = o.CustomerId
  WHERE o.Status = @Status
)
SELECT COUNT(*) FROM Q;
SELECT * FROM Q
ORDER BY Id DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";
using var grid = await conn.QueryMultipleAsync(sql, new { Status = 1, Skip = 0, Take = 50 });
var total = await grid.ReadSingleAsync<int>();
var page  = (await grid.ReadAsync<OrderDto>()).ToList();

// Dapper - 조인 멀티매핑 예시 (Order + Customer)
var sql = @"SELECT o.Id, o.Total, c.Id AS CustomerId, c.Name
            FROM Orders o JOIN Customers c ON c.Id = o.CustomerId
            WHERE o.Status = @Status";
var list = await conn.QueryAsync<Order, Customer, OrderDto>(
    sql,
    (o, c) => new OrderDto { Id = o.Id, Total = o.Total, CustomerName = c.Name },
    new { Status = 1 },
    splitOn: "CustomerId"
);
```

---

마무리로, 제 취향을 투명하게 보여드리자면, 아직 실무에서 두 도구를 모두 깊게 굴려 본 것은 아니고, 주로 샌드박스, 레퍼런스 리딩, 작은 PoC를 바탕으로 정리했습니다. 

음.. 역시 제 취향은 EF Core + Specification 패턴입니다. 쿼리 의도를 코드 가까이에 두고 재사용성을 극대화하면서, 마이그레이션 → 테스트 → 배포까지 한 흐름으로 잇기 편하더라고요.

Stored Procedure나 Dapper는 비상 레버로 남겨 둡니다. 대시보드 응답이 버거워지거나, TVP로 대량 입력이 필요하거나, 플랜 힌트/윈도 함수까지 동원해야 하는 진짜 핫패스가 프로파일링으로 확인될 때만 쓸려구요.

요약하면, 보수적으로는 EF Core, 필요할 땐 Dapper/SP 입니다.

도구 선택은 취향이 아니라 측정이 결정하게 둡니다. 
나중에 실무 경험이 더 쌓이면, 이 원칙과 글도 함께 업데이트하겠습니다. 🙂