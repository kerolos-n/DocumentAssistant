import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Fallback page for any URL that doesn't match a route. Guard-free: everyone sees it. */
@Component({
  imports: [RouterLink],
  selector: 'app-not-found-page',
  templateUrl: './not-found-page.html',
})
export class NotFoundPage {}
