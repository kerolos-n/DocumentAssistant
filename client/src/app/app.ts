import { Component, computed, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RouterOutlet } from '@angular/router';

/** Shape of the JSON returned by the server's `/health` endpoint. */
interface HealthReport {
  readonly status: string;
  readonly durationMs: number;
  readonly checks: readonly { name: string; status: string; description: string | null }[];
}

@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  protected readonly title = signal('client');

  /** Base URL of the ASP.NET Core server (see server/Properties/launchSettings.json). */
  private readonly healthUrl = 'http://localhost:3000/health';

  protected readonly health = httpResource<HealthReport>(() => this.healthUrl);

  protected readonly healthMessage = computed(() => {
    if (this.health.isLoading()) {
      return 'Checking server health…';
    }
    if (this.health.error()) {
      return 'Server is unreachable.';
    }
    const report = this.health.value();
    return report ? `Server is ${report.status.toLowerCase()}.` : 'No health report returned.';
  });
}
