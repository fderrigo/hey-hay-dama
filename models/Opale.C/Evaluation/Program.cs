if (args is ["--diagnose-common", var diagnosisDirectory]) { Opale.C.CommonDiagnosis.Run(diagnosisDirectory); return; }
if (args is ["--audit-common", var commonDirectory]) { Opale.C.CommonAudit.Run(commonDirectory); return; }
if (args is ["--audit", var directory]) { Opale.C.RecordAudit.Run(directory); return; }
if (args.Length != 2) throw new ArgumentException("Usage: Opale.C.Evaluation <new-result-directory> <UTC-deadline>");
Opale.C.Benchmark.Run(args[0], DateTimeOffset.Parse(args[1]));
