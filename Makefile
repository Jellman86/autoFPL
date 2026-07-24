.PHONY: test test-python test-dotnet governance verify container-build container-smoke container-verify

test: test-python test-dotnet

test-python:
	python3 -m unittest discover -s tests -p 'test_*.py' -v

test-dotnet:
	dotnet restore src/backend/AutoFpl.slnx --locked-mode --nologo
	dotnet test src/backend/AutoFpl.slnx --no-restore --configuration Release --nologo

governance:
	python3 tools/governance/check_data_source_policy.py
	python3 tools/governance/check_repository.py
	python3 tools/governance/check_research_review.py
	python3 tools/governance/check_documentation.py

verify: test governance

container-build:
	docker build --build-arg "SOURCE_REVISION=$$(git rev-parse HEAD)" --tag autofpl:local .

container-smoke:
	bash scripts/ci_container_smoke.sh autofpl:local "$$(git rev-parse HEAD)"

container-verify: container-build container-smoke
