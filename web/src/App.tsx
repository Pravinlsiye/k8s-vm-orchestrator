import type { Component } from 'solid-js'
import Nav from './components/Nav'
import Hero from './components/Hero'
import Why from './components/Why'
import Architecture from './components/Architecture'
import Features from './components/Features'
import CodeExamples from './components/CodeExamples'
import QuickStart from './components/QuickStart'
import TechStack from './components/TechStack'
import Footer from './components/Footer'

const App: Component = () => {
  return (
    <div class="relative overflow-x-hidden">
      <div
        aria-hidden
        class="pointer-events-none fixed inset-0 -z-10 dot-grid opacity-30"
      />
      <Nav />
      <main>
        <Hero />
        <Why />
        <Architecture />
        <Features />
        <CodeExamples />
        <QuickStart />
        <TechStack />
      </main>
      <Footer />
    </div>
  )
}

export default App
