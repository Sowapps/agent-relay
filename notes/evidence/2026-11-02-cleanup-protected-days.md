# Cleanup protects recent local calendar days

**Utility:** records Florent's accepted temporal exclusion for the proposed manual maintenance tool.  
**Usage:** apply in later design/implementation together with active/required-data protections; this decision does not authorize real deletion.

Florent: « Pour l'instant, on peut même dire que la journée en cours est protégée de cet outil et si heure <2, la journée précédente est ausis protégée, tu comprends ? »

Assistant: « Oui, selon l’heure locale : La journée en cours est toujours protégée du nettoyage. Avant 02:00, la journée précédente est également protégée. À partir de 02:00, la journée précédente devient éligible, sous réserve des autres protections : fichiers utilisés, opérations ou transferts en cours, etc. Éligible ne signifie pas supprimée automatiquement. »

Florent: « oui, plan ? »

The subsequent GO covers three UI fixes plus inventory/design/prototype of manual cleanup, without real data deletion. Time/day protection cannot be overridden by this tool.
