---
Title: EF Core와 Dapper 데이터 접근 전략
Published: 2025-08-18
Layout: /_layouts/post.cshtml
Excerpt: ORM 추상화의 안정성과 경량 매퍼의 제어, 성능 분석
Tags: [ASP.NET, Data Access, EF Core, Dapper, Architecture]
---

## TL;DR
- EF Core는 **모델 중심 개발과 스키마 이력 관리**에 강하고, Dapper는 **쿼리 제어와 조회 성능**이 돋보입니다.  
- “한 도구로 통일”보다는 **일반 CRUD는 EF Core**, **핫 패스 조회는 Dapper**처럼 역할을 나누면 실무 만족도가 높습니다.  
- EF Core 튜닝 핵심: `.AsNoTracking()`, **프로젝션(Select)**, `AsSplitQuery()`, **CompiledQuery**, 적절한 인덱스.  
- Dapper 운영 핵심: **SQL 일관성 규칙**, `QueryMultiple`/멀티매핑, **TVP(Table-valued Parameter)**, `buffered:false` 스트리밍, 타입 핸들러.  
- 마이그레이션은 EF Core가 기본이고, Dapper라면 **FluentMigrator/DbUp** 같은 도구로 공백을 메우는 편이 좋습니다.  
- 테스트는 EF Core는 **InMemory/SQLite + 컨테이너**, Dapper는 **컨테이너 DB + 스냅샷 픽스처** 조합이 안정적입니다.  
  
---

EF Core는 전통적인 ORM입니다. 도메인 모델과 관계를 코드로 표현하고, LINQ로 의도를 적으면 내부에서 SQL을 생성해 실행합니다. 체인지 트래킹, 로딩 전략(Lazy/Eager/Explicit), 마이그레이션 같은 기능이 긴 호흡의 프로젝트에서 크게 도움이 됩니다. 스키마 변경을 마이그레이션으로 추적하고 배포 파이프라인에 자연스럽게 녹일 수 있어서, 팀 규모가 커질수록 이점이 분명해집니다. 

다만 추상화는 비용을 동반합니다. 복잡한 LINQ가 의도치 않은 조인 폭증이나 N+1을 낳는 경우가 있는데, 대부분의 문제는 몇 가지 습관으로 충분히 완화됩니다. 읽기 경로에는 `.AsNoTracking()`을 적용해 가볍게 가져오고, 프로젝션으로 필요한 컬럼만 선택하며, 다중 Include가 필요할 땐 `AsSplitQuery()`로 쿼리 폭발을 피합니다. 고정 빈도 쿼리는 **CompiledQuery**로 감싸 캐시 비용을 줄이고, EF Core의 `ExecuteUpdate/ExecuteDelete`를 활용하면 라운드트립도 아낄 수 있습니다. 원시 SQL이 필요하면 `FromSqlInterpolated`(안전)과 `FromSqlRaw`(주의)의 차이를 의식해 사용하는 편이 좋습니다. 이 정도 기본기만 갖춰도 CRUD는 성능과 안전성의 균형이 잘 맞습니다. 

Dapper는 경량 매퍼입니다. 개발자가 작성한 SQL이 그대로 실행되고, 결과만 빠르게 객체로 매핑됩니다. 번역 단계가 없으니 성능은 ADO.NET에 가깝고, SQL 힌트·CTE·윈도 함수 등 RDBMS의 기능을 고스란히 활용할 수 있습니다. 대량 조회나 리포팅, 대시보드처럼 읽기 중심 워크로드에서 체감 이득이 큽니다. 다만 스키마 변화에 따른 SQL 유지보수 책임이 개발자에게 남는 만큼, 팀 차원의 규칙과 도구가 필요합니다. 파라미터는 익명 객체/`DynamicParameters`로 일관되게 바인딩하고(보안·플랜 캐시에 유리), 여러 결과셋은 `QueryMultiple`로 왕복 횟수를 줄이며, 대량 입력은 SQL Server 기준 TVP를 통해 배치 처리하는 편이 좋습니다. 큰 결과는 `buffered:false`로 스트리밍하고, 페이징은 커버링 인덱스와 함께 `OFFSET/FETCH`를 사용해 실행 계획을 안정화합니다. 스칼라/값 객체 변환은 TypeHandler로 표준화하면 코드가 단정해집니다.

**트랜잭션 처리 방식**은 EF Core, Dapper 모두 명확합니다. EF Core는 `DbContext.Database.BeginTransaction()`과 실행 전략을 통해 **원자성 + 내결함성**을 다루고, Dapper는 `IDbTransaction`으로 범위를 명시해 커밋/롤백을 관리합니다. 복잡한 비즈니스 연산이라면 EF Core에서 `Databa se.CreateExecutionStrategy()`로 재시도를 감싼 뒤 트랜잭션을 여는 패턴이 안전합니다. 

어느 쪽이든 중요한 건 "업무 규칙이 코드에서 읽히는가" 입니다. 조인 전략 역시 관점의 차이인데, EF Core의 `Include`는 이해하기 쉽지만, 과도하면 중복 행이 늘어 부담이 커집니다. 성능이 중요한 경로에서는 `Select`로 DTO를 직접 투사해 필요한 조인만 표현하는 편이 안정적입니다. Dapper는 처음부터 끝까지 명시적으로 다루니, 읽기 모델을 뷰나 CTE로 미리 단순화해 쿼리를 짧게 유지하면 운영이 편안해집니다.

**운영과 배포**를 생각해보면, EF Core는 **마이그레이션**이 표준입니다. CI에서 마이그레이션 해시를 검증하고, CD 단계에서 `dotnet ef database update`를 실행하는 식으로 관리하면 이력이 투명합니다(앱 기동 시 마이그레이션 자동 적용은 다중 인스턴스 경합 위험이 있어 권장하지 않습니다). Dapper는 기본 제공이 없으므로 **FluentMigrator/DbUp/Flyway** 같은 도구로 파이프라인을 마련해 두면 바람직합니다. 관측성도 관점이 조금 다릅니다. EF Core는 `LogTo`, `EnableSensitiveDataLogging`으로 번역 SQL과 파라미터를 로깅하고, EventCounters로 지표를 노출할 수 있습니다. Dapper는 내장 로거가 없으니, `IDbConnection`/`DbCommand`를 감싼 래퍼로 **실행 시간·행 수·오류**를 표준 포맷으로 남겨두면 추적이 수월합니다.

**테스트 전략**은 현실적으로 다음 조합이 잘 맞습니다. EF Core는 SQLite로 빠른 단위 테스트를, 실제 RDBMS 컨테이너(Testcontainers)로 통합 테스트를 함께 돌리면 번역 차이에서 오는 의외의 실패를 조기에 발견할 수 있습니다. Dapper는 번역 계층이 없으니 처음부터 컨테이너 DB로 가는 편이 안전하고, 여기에 **쿼리 스냅샷 테스트**와 **픽스처 데이터**를 더하면 회귀를 잘 막아줍니다.

**보안과 안정성** 측면에서는 두 도구 모두 파라미터화로 기본기를 잘 갖추고 있습니다. 연결 풀은 ADO.NET 공통 레이어에서 관리되므로, `using` 범위로 연결을 열고 닫아도 풀에 반환되어 부담이 크지 않습니다. 다만 트래픽이 급증해 풀 고갈 신호가 보인다면, 풀 크기를 늘리기 전에 **쿼리 시간 단축**(인덱스, 프로젝션, 페이징)을 먼저 점검하는 편이 효과적입니다. 클라우드 환경에서는 재시도 정책(예: Polly)을 EF Core 실행 전략 또는 Dapper 호출부에 일관되게 적용해, 일시적 오류에 부드럽게 대응하는 것이 좋습니다.

아키텍처를 한 줄로 압축하면, **쓰기는 EF Core, 읽기는 Dapper**가 출발점으로 무난합니다. CQRS를 채택한다면 Command 핸들러는 EF Core로 비즈니스 규칙과 트랜잭션을 표현하고, Query 핸들러는 Dapper로 화면/리포트 요구에 맞춘 **읽기 모델**을 구성하는 식이 자연스럽습니다. 마이크로서비스라면 서비스 성격에 따라 선택을 달리해도 좋습니다. 무결성이 중요한 코어 도메인은 EF Core, 읽기 최적화가 핵심인 보조 도메인은 Dapper처럼요. 중요한 건 원칙을 코드와 파이프라인에 녹여 **일관성 있게 반복**하는 습관입니다.

간단한 예시로 분위기만 살짝 보시죠.

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

마무리로, 제 취향을 투명하게 적습니다. 아직 실무에서 두 도구를 모두 깊게 굴려본 것은 아니에요. 다만 샌드박스나 레퍼런스 리딩을 기준으로 하면, 기본값은 EF Core + Specification 패턴입니다. 쿼리 의도를 코드 가까이에 두고, 마이그레이션·테스트·배포 흐름까지 한 줄로 꿰기 좋거든요.

Stored Procedure나 Dapper는 비상 레버처럼 남겨둡니다. 대시보드가 숨이 차거나, TVP로 대량 입력을 퍼부어야 하거나, 플랜 힌트까지 필요해지는 진짜 핫 패스가 보이면 그때만 씁니다. 그 경우에도 경계는 분명히 입출력 DTO, 파라미터 바인딩, 로깅 규칙을 정해 이탈 비용을 낮춥니다.

요약하면, 보수적으로는 EF Core, 필요할 땐 Dapper/SP

실무 경험이 더 쌓일 때까지는 이 원칙으로 움직이려 합니다.   
(경험이 늘면, 결론도 기꺼이 업데이트하겠습니다.)