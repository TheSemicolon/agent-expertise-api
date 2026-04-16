## Summary

<!-- 1-3 sentences describing what this PR does and why -->

## Type of Change

- [ ] `feat` — new feature
- [ ] `fix` — bug fix
- [ ] `docs` — documentation only
- [ ] `chore` — maintenance
- [ ] `refactor` — restructuring without behavior change
- [ ] `test` — adding or updating tests
- [ ] `ci` — CI/CD changes
- [ ] `style` — formatting or linting fixes
- [ ] Breaking change (add `!` to PR title)

## Test Plan

- [ ] `dotnet test` passes locally (requires Docker for integration tests)
- [ ] New features and bug fixes include tests
- [ ] Helm render tests pass (if chart changed): `bash helm/expertise-api/tests/test-render.sh`
- [ ] Relevant endpoint(s) manually verified via curl or Scalar UI (for API changes)

## Checklist

- [ ] No secrets or credentials committed
- [ ] Linter clean on changed files
- [ ] Database migrations are reversible (if applicable)
- [ ] API changes are backward-compatible (if applicable)
- [ ] CLAUDE.md updated (if commands, endpoints, or workflow changed)
