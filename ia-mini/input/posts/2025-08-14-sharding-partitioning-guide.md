---
Title: 대규모 데이터베이스 확장 전략 — 샤딩과 파티셔닝
Published: 2025-08-14
Layout: /_layouts/post.cshtml
Excerpt: 수평 확장(샤딩)과 파티셔닝의 개념, 샤드 키 선택, 설계/운영 패턴, 리밸런싱, 트랜잭션/조인 대책 — ASP.NET 3년차, AZ-900 막 취득한 개발자 기준 친절 가이드
Tags: [Azure in action]
---

'언제 **파티셔닝**으로 충분하고, 언제 **샤딩**이 필요하며, Azure에서 어떻게 구현하고 운영해야 하는지'를 단계별로 정리합니다.

---

## TL;DR

- 읽기 부하는 **리드 레플리카/캐시**, 쓰기·저장 한계는 **샤딩**으로 해결하는 게 정석.
- **샤드 키 선택**이 절반 이상을 결정한다: **균등 분포 + 자주 쓰는 쿼리 경로와 일치 + 불변성**.
- 운영은 **가시성(관측)** 과 **리밸런싱 자동화**가 성패를 가른다.
- 처음부터 샤딩하지 말고 **캐시 → 리드 레플리카 → 파티셔닝 → 샤딩** 순서로 성숙도를 올려라.
- Azure SQL은 **Hyperscale**로 한계를 늦출 수 있고, **Cosmos DB**는 **파티션 키** 설계가 전부다.

---

## 왜 확장이 필요한가

- **사용자/트래픽 증가**로 단일 DB의 CPU, IOPS, 메모리가 포화.
- **데이터 폭증**으로 인덱스 관리, 백업/복구, DDL(스키마 변경) 시간이 기하급수 증가.
- **SLA**를 맞추려면 **지연(latency)** 상한과 **가용성** 목표를 꾸준히 지켜야 함.

핵심은 **처리량(throughput)** 을 키우되 **지연은 낮추고**, 장애 시 **빨리 복구**하는 것.

---

## 수직 vs 수평 확장

- **수직 확장(Scale-Up)**: 같은 DB를 더 큰 사양으로. 간단하지만 상한이 뚜렷하고 비용 증가가 가파름.
- **수평 확장(Scale-Out)**: 데이터를 여러 노드(샤드)로 쪼개 분산. 운영은 복잡해지지만 **선형에 가까운 확장성**.

> **실무 팁**: **읽기**는 캐시/리드 레플리카로, **쓰기·저장**은 샤딩으로 해결하는 것이 흔한 패턴.

---

## 파티셔닝 vs 샤딩 (용어 정리)

- **파티셔닝(Partitioning)**: **한 DB 인스턴스 내부**에서 테이블을 파티션으로 나눔. (DB 기능)
- **샤딩(Sharding)**: **여러 DB 인스턴스**에 데이터를 나눠 저장하는 **수평 파티셔닝**. (앱/미들웨어 라우팅 필요)

둘은 대체 관계가 아니라 **보완** 관계. **샤딩 후 각 샤드 내부에서 파티셔닝**을 병행하기도 한다.

### Azure에서의 적용

- **Azure SQL Database**  
  - 파티셔닝: **Partition Function/Scheme**로 범위 분할, **슬라이딩 윈도우** 가능.  
  - 수직 확장: vCore/DTU 변경, **Hyperscale**로 스토리지 대용량(수십~수백 TB급)과 빠른 확장.  
  - 읽기 부하: **Readable Secondary**(Hyperscale), **Elastic Pool**로 비용 최적화.

- **Azure Cosmos DB (Core/SQL)**  
  - 샤딩 = 서비스 내장. **파티션 키**를 고르면 시스템이 자동 분산.  
  - **크로스 파티션 쿼리 비용**과 **핫 파티션**에 유의. **변경 불가**에 가깝기 때문에 키 설계가 중요.

- **Azure Database for PostgreSQL - Flexible Server / Cosmos DB for PostgreSQL(Citus)**  
  - **Citus**는 분산(Postgres 샤딩) 기능 제공. 분산 컬럼(=샤드 키) 중심 설계.

---

## 샤딩 기본 설계

**비유**: “한 창구에만 줄을 세우면 느려진다.” 고객(트래픽)을 **여러 창구(샤드)** 로 나눠 받으면 전체 처리량이 올라간다.  
단, **고객을 어떤 기준으로 어느 창구에 보낼지(샤드 키/라우팅)** 가 중요.

### 샤드 키 선택 가이드 (현실적인 체크리스트)

좋은 샤드 키는 **데이터 분산**과 **쿼리 패턴**을 동시에 만족해야 한다.

1. **높은 카디널리티**: 다양한 값으로 골고루 분산. (핫샤드 방지)  
2. **균등 분포**: 특정 샤드에 저장/트래픽이 몰리지 않음.  
3. **쿼리 경로와 일치**: 가장 자주 쓰는 조회/조인 조건과 맞물려 **싱글 샤드 쿼리**가 되게.  
4. **불변성**: 샤드 키가 바뀌면 재분배 비용 폭증.  
5. **시간 스큐 방지**: 단조 증가 키(IDENTITY, 순증 타임스탬프)는 **한 샤드로 쓰기 집중** 유발.

**자주 쓰는 선택지**  
- **B2C**: `UserId` (또는 `Hash(UserId)`)  
- **멀티테넌시(B2B)**: `TenantId` (대형 테넌트는 전용 샤드 고려)  
- **로그/이벤트**: `Hash(SourceId)` + **시간 버킷**(예: 월 단위)

> **실무 팁**: *가상 샤드(virtual shard)* — 해시 버킷을 **물리 샤드보다 더 많이** 만든 뒤, 버킷→물리 샤드 매핑으로 관리하면 **리밸런싱이 쉬워진다**.

### 샤딩 방식 비교 (언제 무엇을 고르나)

1. **범위 샤딩(Range)**  
   - 예: `UserId 0~1M → Shard A`, `1M~2M → Shard B`  
   - 장점: 범위 스캔·정렬·페이징 유리  
   - 단점: 키 분포 치우치면 핫스팟 유발, 리밸런싱 때 범위 재조정 필요  
   - **적합**: 시간순 정렬/리포트가 많고, 키 분포가 비교적 균일할 때

2. **해시 샤딩(Hash)**  
   - 예: `shard = Hash(UserId) % N`  
   - 장점: **균등 분산** 강함  
   - 단점: 범위 쿼리 불리, 샤드 수 변경 시 재해시 부담  
   - **적합**: OLTP 트랜잭션 중심, 키 스큐 가능성이 높을 때

3. **디렉터리(룩업) 샤딩(Directory/Lookup)**  
   - 중앙 **샤드 맵 테이블**에 `(Key → Shard)` 매핑 유지  
   - 장점: **유연한 재배치**(특정 테넌트/레코드 이동)  
   - 단점: 디렉터리 가용성·일관성 설계가 중요(SPOF 주의)  
   - **적합**: 멀티테넌시, **과열 테넌트 분리** 자주 필요한 SaaS

> **현실적 조합**: **해시 + 디렉터리** — 기본은 해시 분산, **예외 테넌트만** 디렉터리로 별도 배치.

---

## 라우팅(샤드 맵) 패턴

- **클라이언트 라우팅**: 앱이 샤드 맵을 캐싱해 직접 라우팅(레이턴시↓). 캐시 무효화 전략 필수.  
- **미들웨어/프록시 라우팅**: 중간 계층이 라우팅(클라이언트 단순화). 중간층의 확장성/HA 필요.  
- **DB 내장형 라우터**: 일부 솔루션(Vitess/Citus 등)은 라우팅을 내장 제공.

샤드 맵(메타)은 대개 아래 필드를 가진다.

| 컬럼 | 설명 |
|---|---|
| `ShardId` | 샤드 고유 식별자 |
| `KeyRange`/`HashRange` | 담당 범위(또는 버킷) |
| `Endpoint` | 연결 문자열/호스트 |
| `State` | Online/Draining/Rebalancing |
| `Version` | 맵 버전(원자적 교체/롤백용) |

---

## 리밸런싱(재분배) — 다운타임 최소화 절차

언제 하나? **저장/트래픽 불균형**, **샤드 추가**, **과열 테넌트 격리**가 필요할 때.

1. **플래닝**: 대상 범위/테넌트 선정, 목표 샤드 결정  
2. **Draining**: 대상 샤드/키에 **쓰기 제한** 후 트래픽 서서히 빼기  
3. **증분 복제 + 컷오버**: 스냅샷 → 변경분(CDC/Change Tracking) → 짧은 정지로 전환  
4. **검증**: 카운트/체크섬/샘플링으로 데이터 동등성 확인  
5. **샤드 맵 원자 교체**: `Version` 증가시키며 업데이트  
6. **롤백 플랜**: 실패 시 역방향 컷오버

> **핵심**: **Dual-write** 기간을 짧게, **Idempotent upsert**로 재시도 안전성 확보.

### Azure 관점 팁

- **Azure SQL**: **DMS(Database Migration Service)**, **Change Tracking/CDC**, **별도 읽기 복제본**을 활용한 저지연 컷오버 설계.  
- **Cosmos DB**: 파티션 키 변경은 사실상 **새 컨테이너로 마이그레이션**이므로 초기 설계 신중히.

---

## 트랜잭션·조인·크로스 샤드 쿼리 다루기

- **싱글 샤드 트랜잭션**을 목표로 쿼리 경로에 **샤드 키 포함**.  
- **크로스 샤드 쿼리**는 비용이 크다. 불가피하면:  
  - **팬아웃 리드**(모든 샤드 병렬 조회→집계) + 타임아웃/부분 결과 허용  
  - **사전 집계/머티리얼라이즈드 뷰**로 빈도 낮추기  
  - **2PC**는 정말 필요한, 작고 드문 트랜잭션에 한정

**조인 전략**  
- **공샤딩(Co-sharding)**: 자주 조인하는 테이블을 같은 키로 같은 샤드에  
- **참조 데이터 복제**: 작은 테이블은 모든 샤드에 복제  
- **애플리케이션 레벨 조인**: 키로 개별 조회 후 앱에서 머지

---

## 멀티테넌시 격리 전략

- **공유 샤드(Soft)**: 여러 테넌트를 한 샤드에. 자원 효율 ↑, 간섭 위험 존재.  
- **전용 샤드(Hard)**: 대형·과열 테넌트 전용. 예측 가능성 ↑, 운영비 ↑.  
- **하이브리드**: 기본은 공유, **문제 테넌트만 분리**.

**지역/규제(Compliance)** 요구가 있으면 **샤드 경계 = 지역 경계**로 설계.

---

## 장애·복구·일관성

- **복제**: 동기/비동기 조합으로 RPO/RTO와 쓰기 지연 균형.  
- **장애 전파 차단**: 회로 차단기 + 재시도 + 백오프.  
- **일관성 모델**: 강일관성↔최종일관성, **도메인별 선택**.  
- **백업/복구**: 샤드 단위 스냅샷 + 로그 기반 시점 복구. **복구 리허설 자동화** 권장.

### ASP.NET에서의 회복 탄력성(예: Polly)

```csharp
var retry = Policy
    .Handle<SqlException>()
    .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(100 * i));

var breaker = Policy
    .Handle<SqlException>()
    .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

var resilient = Policy.WrapAsync(retry, breaker);

await resilient.ExecuteAsync(() => ExecuteShardQueryAsync());
```

---

## 마이그레이션 단계별 체크리스트 (현실 버전)

1. **워크로드 분석**: 상위 20% 쿼리/트랜잭션, p95/p99, 키 분포, 핫 테이블 확인  
2. **샤드 키/전략 결정**: (범위/해시/디렉터리) + 공샤딩 필요성 판단  
3. **스키마/인덱스 설계**: 샤드 키를 **선행 컬럼**으로, 보조 인덱스/파티션 키 정리  
4. **라우터/샤드 맵 구현**: 캐시(TTL/버전), 장애 폴백, 스키마·시드 자동화  
5. **데이터 이행 계획**: 스냅샷→CDC→컷오버, **리허설 2~3회** 이상  
6. **관측성**: 샤드별 QPS, p95/p99, 에러율, 리드/라이트 분포, 리밸런싱 진행률 대시보드  
7. **런북/롤백**: 컷오버 실패·지연·데이터 불일치 대응 시나리오

---

## 운영 팁 & 모니터링 지표 (대시보드 기준)

- **핫샤드 탐지**: 샤드별 QPS/IO/잠금 대기/큐 길이, 임계 초과 알람  
- **키 스큐**: 상위 N 키/테넌트의 트래픽 비중  
- **리밸런싱 헬스**: 이동량, 동기화 지연, 재시도율  
- **에러 버짓**: 크로스 샤드 쿼리 비율 상한  
- **용량 계획**: 주간 성장률 기반 **90일 선제 증설**

---

## Azure in Action (구체 시나리오)

### 1) Azure SQL로 시작해 한계를 늦추기
- **단일 DB + 파티셔닝**으로 운영 → 성능·용량 한계가 보이면  
- **Hyperscale**로 이전(스토리지·읽기 확장) → 그럼에도 **쓰기가 포화**되면  
- **샤딩(다중 DB)** 도입 + 각 샤드 내부 파티셔닝
- 비용 최적화: **Elastic Pool**로 다수 샤드의 vCore/DTU를 풀링

### 2) Cosmos DB에서의 파티션 키 설계
- **높은 카디널리티**의 키(예: `tenantId`, `userId`) 선택
- **핫 파티션** 방지 위해 **해시 성격**의 키 고려, **시간 키는 보조 인덱스**로
- 크로스 파티션 쿼리는 RU 소모↑ → **쿼리 경로에 파티션 키 포함** 습관화
- 파티션 키는 **사실상 변경 불가**이므로 초기에 검증 철저

### 3) PostgreSQL(Citus)로 분산 트랜잭션 OLTP
- **분산 컬럼**을 정하고 테이블을 분산 객체로 생성
- 자주 조인하는 테이블은 **공샤딩** 또는 **참조 테이블** 복제

---

## 샘플 스키마 & 코드 스니펫

### (SQL Server) 파티션 함수/스킴 예시

```sql
-- 월 단위 파티션 (예: 2024-01 ~ 2026-12)
CREATE PARTITION FUNCTION pf_OrderByMonth (DATE)
AS RANGE RIGHT FOR VALUES (
  '2024-02-01', '2024-03-01', '2024-04-01', '2024-05-01',
  '2024-06-01', '2024-07-01', '2024-08-01', '2024-09-01',
  '2024-10-01', '2024-11-01', '2024-12-01',
  '2025-01-01', '2025-02-01', '2025-03-01', '2025-04-01',
  '2025-05-01', '2025-06-01', '2025-07-01', '2025-08-01',
  '2025-09-01', '2025-10-01', '2025-11-01', '2025-12-01',
  '2026-01-01', '2026-02-01', '2026-03-01', '2026-04-01',
  '2026-05-01', '2026-06-01', '2026-07-01', '2026-08-01',
  '2026-09-01', '2026-10-01', '2026-11-01', '2026-12-01'
);

CREATE PARTITION SCHEME ps_OrderByMonth
AS PARTITION pf_OrderByMonth
ALL TO ([PRIMARY]);

CREATE TABLE Orders (
  OrderId      BIGINT NOT NULL PRIMARY KEY,
  TenantId     BIGINT NOT NULL,
  CreatedAt    DATE   NOT NULL,
  Amount       DECIMAL(18,2) NOT NULL
) ON ps_OrderByMonth(CreatedAt);

CREATE INDEX IX_Orders_Tenant_Created
  ON Orders(TenantId, CreatedAt)
  ON ps_OrderByMonth(CreatedAt);
```

### 샤드 맵 & 테넌트 디렉터리

```sql
CREATE TABLE ShardMap (
  ShardId       INT PRIMARY KEY,
  HashStart     INT NOT NULL,
  HashEnd       INT NOT NULL,
  Endpoint      VARCHAR(255) NOT NULL,   -- "Server=...;Database=Shard_12;..."
  State         VARCHAR(16) NOT NULL,    -- Online | Draining | Rebalancing
  Version       BIGINT NOT NULL
);
CREATE UNIQUE INDEX IX_Range ON ShardMap(HashStart, HashEnd);

CREATE TABLE TenantDirectory (
  TenantId  BIGINT PRIMARY KEY,
  ShardId   INT NOT NULL,
  UpdatedAt DATETIME2 DEFAULT SYSUTCDATETIME()
);
```

### ASP.NET (EF Core) 라우팅 의사코드

```csharp
public interface IShardResolver {
    (int ShardId, string Endpoint) Resolve(long tenantId);
}

public class DirectoryFirstShardResolver : IShardResolver {
    private readonly IShardDirectoryCache dir;
    private readonly IShardMapCache map;
    private readonly int shardCount;
    public (int, string) Resolve(long tenantId) {
        var shardId = dir.TryGetShard(tenantId) 
            ?? (Hash(tenantId) % shardCount);
        var endpoint = map.Resolve(shardId); // versioned
        return (shardId, endpoint);
    }
}

public class ShardedDbContextFactory {
    private readonly IShardResolver resolver;
    public AppDbContext Create(long tenantId) {
        var (_, endpoint) = resolver.Resolve(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(endpoint)
            .Options;
        return new AppDbContext(options);
    }
}
```

### 팬아웃 리드(집계)

```csharp
var tasks = shards.Select(s => CountOrdersAsync(s, since));
var counts = await Task.WhenAll(tasks);
var total = counts.Sum();
```

---

## 비용·보안·운영 현실 체크

- **비용**: 샤드가 늘수록 **모니터링/백업/자동화**가 필수. Elastic Pool, 자동 스케일, 예약 인스턴스 등으로 최적화.  
- **보안**: **Managed Identity + Key Vault**로 샤드별 연결 문자열/비밀 분리.  
- **데이터 거버넌스**: 지역별 샤딩, **Always Encrypted/연결 암호화** 고려.

---

## 자주 묻는 질문(FAQ)

- **Q. 언제부터 샤딩을 시작해야 하나요?**  
  A. 캐시·리드 레플리카·파티셔닝으로도 **SLO를 못 맞출 때**, 그리고 **3~6개월 내 성장이 확실**할 때.

- **Q. 샤드 키를 잘못 골랐습니다.**  
  A. 리밸런싱 자동화 파이프라인을 만들어 **단계적 재분배**. Cosmos DB는 **새 컨테이너로 마이그레이션**.

- **Q. 크로스 샤드 트랜잭션이 자주 필요합니다.**  
  A. **도메인 재설계**를 먼저 검토. 불가피하면 범위를 좁히고 **Outbox/Idempotency**로 보상 트랜잭션.

---

## 요약 정리

1. 먼저 **캐시/레플리카/파티셔닝**으로 버텨라.  
2. **샤드 키**가 80%다 — 균등 분포, 쿼리 경로 일치, 불변성.  
3. **해시+디렉터리** 조합이 현실적이며 **가상 샤드**로 리밸런싱을 쉽게.  
4. **관측성·자동화** 없인 샤딩은 고통. 대시보드/런북 필수.  
5. Azure에선 **SQL Hyperscale**, **Cosmos 파티션 키**, **Citus** 등 옵션을 비교·선택.