.PHONY: test governance verify

test:
	python3 -m unittest discover -s tests -p 'test_*.py' -v

governance:
	python3 tools/governance/check_repository.py

verify: test governance
