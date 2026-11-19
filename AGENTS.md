# AGENTS

## 한국어

- CLI 동작을 바꾸기 전에 [한국어 README](README.ko.md)를 읽고, 변경된 사용법은 영어 문서에도 반영합니다.
- 용어와 명령은 Git의 워크트리(worktree), 커밋(commit), 푸시(push), 병합(merge) 의미를 따릅니다.
- C# 작업은 `guidelines/blackthunder-conventions`의 C# 컨벤션과 플랫폼 지침을 따릅니다. 문서 작업은 문서 컨벤션과 번역 지침을 따릅니다.
- 서브모듈은 `git submodule update --init --recursive`로 초기화합니다. UniTest는 테스트 프로젝트에서 참조합니다.
- 코드 주석과 Git 커밋 제목은 영어로 작성합니다. 코드와 프로젝트 파일은 LF로 저장합니다.
- 동작을 바꿀 때는 관련 테스트를 실행하고, 배포 전에 ESOT 전체 테스트를 실행합니다.

## English

- Read the [English README](README.md) before changing CLI behavior. Update both language editions when usage changes.
- Use Git terminology for worktrees, commits, pushes and merges. Preserve the meaning of each command.
- Follow the C# convention and platform instructions under `guidelines/blackthunder-conventions`. Follow the documentation convention and translation instructions when editing guides.
- Initialize submodules with `git submodule update --init --recursive`. Reference UniTest from the test project.
- Use English for code comments and Git commit titles. Save source and project files with LF line endings.
- Test affected behavior when making changes and run the full ESOT suite before publishing.
