"""Leakage-safe local analytics for autoFPL."""

from .baseline import EvaluationError, evaluate_database

__all__ = ["EvaluationError", "evaluate_database"]
