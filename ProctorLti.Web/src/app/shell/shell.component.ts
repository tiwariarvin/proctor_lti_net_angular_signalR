import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { LaunchBoot, SessionService } from '../services/session.service';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly sessions = inject(SessionService);

  boot: LaunchBoot | null = null;
  loadError: string | null = null;
  status = 'Loading…';
  shellPause = false;
  private quizWindow: Window | null = null;
  private pollId: ReturnType<typeof setInterval> | null = null;
  private sub: Subscription | null = null;

  ngOnInit(): void {
    const sid = this.route.snapshot.queryParamMap.get('sid');
    if (!sid) {
      this.loadError = 'Missing session id (sid).';
      this.status = 'Error';
      return;
    }

    this.sub = this.sessions.getSession(sid).subscribe({
      next: (b) => {
        this.boot = b;
        this.loadError = null;
        if (!b.testRunnerUrl) {
          this.status = 'Missing URL';
        } else {
          this.status = 'Use Open quiz to begin';
        }
        this.startPoll();
      },
      error: () => {
        this.loadError = 'Could not load session.';
        this.status = 'Error';
      },
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    if (this.pollId != null) clearInterval(this.pollId);
  }

  private hasOpenQuizTab(): boolean {
    return !!this.quizWindow && !this.quizWindow.closed;
  }

  private startPoll(): void {
    this.pollId = setInterval(() => {
      if (this.quizWindow && this.quizWindow.closed) {
        this.quizWindow = null;
        this.shellPause = false;
        this.status = 'Quiz tab was closed';
      }
    }, 1000);
  }

  openQuiz(): void {
    const url = this.boot?.testRunnerUrl;
    if (!url) return;
    if (this.hasOpenQuizTab() && !window.confirm('Close the current quiz window and open a new one?')) {
      return;
    }
    if (this.hasOpenQuizTab()) {
      try {
        this.quizWindow?.close();
      } catch {
        /* ignore */
      }
      this.quizWindow = null;
    }
    this.quizWindow = window.open(url, 'd2l_lti_proctor_quiz', 'noopener,noreferrer');
    if (!this.quizWindow) {
      this.status = 'Could not open tab (popup may be blocked)';
      return;
    }
    this.shellPause = false;
    this.status = 'Running (quiz tab)';
  }

  play(): void {
    if (!this.hasOpenQuizTab()) {
      this.status = 'No quiz tab — use Open quiz first';
      return;
    }
    try {
      this.quizWindow?.focus();
      this.status = 'Running (quiz tab)';
      this.shellPause = false;
    } catch {
      this.status = 'Could not focus quiz window';
    }
  }

  pause(): void {
    if (!this.hasOpenQuizTab()) return;
    this.status = 'Pause: install the proctor extension to block interaction in the Brightspace tab';
  }

  stop(): void {
    if (!this.hasOpenQuizTab()) return;
    try {
      this.quizWindow?.close();
    } catch {
      /* ignore */
    }
    if (this.quizWindow && !this.quizWindow.closed) {
      this.status = 'Stop: install the proctor extension to close the quiz tab';
      return;
    }
    this.quizWindow = null;
    this.shellPause = false;
    this.status = 'Stopped (quiz tab closed)';
  }
}
