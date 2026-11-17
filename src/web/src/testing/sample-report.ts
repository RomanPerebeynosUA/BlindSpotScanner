import sample from '../../../../BlindSpotScanner-Blueprint/examples/sample-report.json';
import { IntegrationReport } from '../app/core/api/report';

/** A fresh copy of the blueprint's synthetic example report (CLAUDE.md: examples/ as fixtures). */
export const sampleReport = (): IntegrationReport => structuredClone(sample) as IntegrationReport;
