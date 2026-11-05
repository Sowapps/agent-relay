# Targeted maintenance references

**Date:** 2026-11-05  
**Source:** Florent, coupled-operation maintenance requirement.

> Florent: « Egalement, un manque de référence entre eux et de @see peut être à l'origine du probleme. Faut pas abuser avec ces outils (ref et @see) mais il faut en mettre. Un close et un open se référence mutuellement, c'est la base, ce n'est qu'un exemple parmis les innombrables cas. »

**Settled:** operations that must evolve together expose that relationship through targeted references, including reciprocal open/close references. Explain the shared invariant where useful. References complement shared implementation and checks; they do not replace them or require a link for every ordinary call.
