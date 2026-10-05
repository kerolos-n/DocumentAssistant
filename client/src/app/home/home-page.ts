import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Public landing page for signed-out visitors. Signed-in users never see it (homeGuard). */
@Component({
  imports: [RouterLink],
  selector: 'app-home-page',
  templateUrl: './home-page.html',
})
export class HomePage {}
