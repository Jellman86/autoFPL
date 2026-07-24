.PHONY: test test-python test-dotnet governance verify

test: test-python test-dotnet

test-python:
	python3 -m unittest discover -s tests -p 'test_*.py' -v

test-dotnet:
	dotnet restore src/backend/AutoFpl.slnx --locked-mode --nologo
	dotnet test src/backend/AutoFpl.slnx --no-restore --configuration Release --nologo

governance:
	python3 tools/governance/check_repository.py
	python3 tools/governance/check_research_review.py
	python3 tools/governance/check_documentation.py

verify: test governance
