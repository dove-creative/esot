# ESOT

[English](README.md)

ESOT(Equal Sources of Truth)는 같은 리소스 묶음을 여러 로컬 워크트리에서 독립적으로 편집하는 CLI입니다. 워크트리마다 현재 파일과 과거 원본의 보관 한도를 관리합니다. Git은 파일 경로와 SHA-256을 커밋하고, ESOT는 실제 파일을 각 워크트리에 보관합니다.

[프로젝트 개요](docs/README.ko.md) · [저장 구조와 복구](docs/Architecture.ko.md) · [테스트와 실행 결과](tests/README.ko.md)

## 설치

Git과 .NET 10 SDK가 필요합니다. 소스에서 .NET 도구를 빌드해 설치합니다.

```powershell
git clone --recurse-submodules https://github.com/dove-creative/esot.git
cd esot
dotnet pack src/Esot -c Release -o artifacts/packages
dotnet tool install --global --source ./artifacts/packages ESOT --version 0.1.1
esot --help
```

## 첫 워크트리와 커밋

다음 예시는 현재 폴더 아래의 Common을 초기화하고 옆에 빈 Unity 폴더를 작업본으로 추가합니다. 초기 커밋에는 Common에 이미 있는 파일이 들어갑니다. `*.meta`는 모든 깊이에서 제외됩니다. 기존 Unity 폴더도 등록할 수 있습니다. 제외 규칙에 걸리지 않는 기존 파일은 원본 커밋과 바이트가 일치해야 하며, 다른 파일이 있으면 이름을 출력하고 중단합니다.

```powershell
esot init Common ./Common --limit 10GB --ignore "*.meta"
esot -C ./Common worktree add Unity ../Unity --limit 0
```

Unity 워크트리에서 토끼 PSD를 편집한 뒤 Common으로 병합합니다.

```powershell
esot -C ./Unity status
esot -C ./Unity commit -m "Add rabbit ears"
esot -C ./Common merge Unity
esot -C ./Common log
```

대상에서도 같은 PSD를 다르게 편집하고 커밋했다면 종료 코드 2와 충돌 경로가 출력되며 양쪽 파일과 브랜치가 유지됩니다. 대상의 파일을 선택하려면 `esot -C ./Common merge Unity --ours "토끼.psd"`, 보낸 쪽의 파일을 선택하려면 `--theirs "토끼.psd"`를 사용합니다. 여러 충돌은 경로마다 선택합니다. 병합 성공 시 양쪽에 보낸 기록과 받은 기록이 남습니다.

## 푸시, 동기화와 보관 한도

`push`는 대상 브랜치를 빨리 감기(fast-forward)할 수 있을 때 전진시킵니다. `sync`는 지정한 워크트리를 공통 결과로 병합하며 이름을 생략하면 등록된 워크트리 전체를 대상으로 합니다. 어느 한쪽에 커밋하지 않은 변경이 있으면 먼저 커밋해야 합니다.

```powershell
esot -C ./Unity push Common
esot -C ./Common worktree add Art ../Art
esot -C ./Common sync Common Unity Art
esot -C ./Common config storage.limit 5GB
esot -C ./Common gc
```

워크트리별 기본 한도는 과거 파일 내용 10GB(10,000,000,000바이트)입니다. 0은 과거 내용 보관량 0바이트입니다. 현재 커밋을 복원할 사본은 항상 한도 계산에서 제외됩니다. 한도를 초과하면 해당 워크트리에서 가장 먼저 과거 버전이 된 사본부터 정리합니다. 마지막 사본이 삭제된 버전은 이력과 해시만 조회할 수 있습니다. Unity에서만 보유한 과거 사본이 Common으로 자동 전송되지는 않습니다. 보관된 파일을 복원하려면 `esot log`에서 커밋을 찾고 `esot restore --source <commit> "토끼.psd"`를 실행합니다. 등록된 워크트리에 남아 있는 사본을 가져와 작업 변경으로 복원합니다.

지정 경로의 `.worktree/worktree.json`에는 워크트리 이름과 공유 메타데이터 저장소 위치가 있고, `.storage`에는 실제 파일 내용이 있습니다. 공유 저장소는 기본적으로 사용자 로컬 앱 데이터 아래 `ESOT`에 생성됩니다. `init` 전에 `ESOT_HOME`을 지정하면 위치를 선택할 수 있습니다. 백업할 때는 이 저장소와 각 워크트리를 함께 보관합니다. 이 버전은 한 PC에서 고정된 폴더 경로를 사용합니다.

## 자동화와 테스트

`--lang ko` 또는 `--lang en`으로 메시지 언어를 선택할 수 있습니다. 종료 코드는 성공 0, 오류 1, 충돌 또는 빨리 감기 거부 2입니다. 파일 반영 중 중단되면 어느 등록 워크트리에서든 `esot recover`로 재개합니다. 외부 편집으로 예상 내용이 바뀐 파일을 만나면 중단하고 경로를 출력합니다.

```powershell
dotnet build ESOT.slnx -c Release
dotnet run --project tests/Esot.Tests -c Release
```

[UniTest](dependencies/unitest)는 병합 규칙을 검증하는 개발 의존성입니다. CLI 통합 테스트는 임시 폴더에서 한글 파일명, 충돌, 삭제, 세 워크트리 동기화, 보관 한도를 검증합니다. 공통 작업 규칙은 [BlackThunder Conventions](guidelines/blackthunder-conventions)에 있습니다.

## 라이선스

ESOT는 [MIT 라이선스](LICENSE)로 배포됩니다.
